using Core.Graph.Identity;
using Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Graph.Analysis.Analyzers;

public sealed class AspNetRouteAnalyzer : IGraphAnalyzer
{
    public string Name => "aspnet-route";

    private static readonly HashSet<string> HttpMethodAttributes = new(StringComparer.Ordinal)
    {
        "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch"
    };

    private static readonly Dictionary<string, string> AttributeToHttpMethod = new(StringComparer.Ordinal)
    {
        ["HttpGet"] = "GET",
        ["HttpPost"] = "POST",
        ["HttpPut"] = "PUT",
        ["HttpDelete"] = "DELETE",
        ["HttpPatch"] = "PATCH"
    };

    public void Analyze(GraphAnalysisContext context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in context.UnitsByFile)
        {
            var fileUnits = group.ToList();
            if (fileUnits.Count == 0)
                continue;

            var absolutePath = fileUnits[0].FilePath;
            var relativePath = fileUnits[0].RelativeFilePath;
            var projectPath = fileUnits[0].ProjectPath;

            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                continue;

            var sourceText = File.ReadAllText(absolutePath);
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();

            // 预先建立该文件内 methodId → CodeUnit 的索引
            var unitById = fileUnits.ToDictionary(u => u.Id, StringComparer.Ordinal);

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsController(classDecl))
                    continue;

                var namespaceName = GetNamespace(classDecl);
                var className = GetClassName(classDecl);
                var framework = DetectFramework(classDecl);
                var baseRoute = GetClassRoute(classDecl, className);

                foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (!IsAction(method, className))
                        continue;

                    var methodName = method.Identifier.Text;
                    var paramTypes = GetParameterTypes(method);
                    var methodId = MethodIdBuilder
                        .FromMethod(projectPath, namespaceName, className, methodName, paramTypes)
                        .Value;

                    // 跳过已处理的方法（同一方法在多处 partial 文件中可能重复）
                    if (!seen.Add(methodId))
                        continue;

                    // 仅匹配已在扫描结果中存在的方法
                    if (!context.NodesById.ContainsKey(methodId))
                        continue;

                    var httpMethod = GetHttpMethod(method, methodName);
                    var actionTemplate = GetActionRouteTemplate(method, methodName);
                    var fullRoute = BuildFullRoute(baseRoute, actionTemplate, className, methodName);

                    var data = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["route"] = fullRoute,
                        ["httpMethod"] = httpMethod,
                        ["controller"] = className,
                        ["action"] = methodName,
                        ["framework"] = framework
                    };

                    context.AddFact(
                        subjectId: methodId,
                        factType: "http-route",
                        subjectKind: GraphSubjectKinds.Method,
                        sourceFile: relativePath,
                        data: data);

                    context.AddAnnotation(methodId, "route", fullRoute, relativePath);
                    context.AddAnnotation(methodId, "http-method", httpMethod, relativePath);
                    context.AddAnnotation(methodId, "entry-point", "true", relativePath);
                }
            }
        }
    }

    private static bool IsController(ClassDeclarationSyntax classDecl)
    {
        if (HasAttribute(classDecl, "ApiController"))
            return true;

        var className = classDecl.Identifier.Text;
        if (className.EndsWith("Controller", StringComparison.Ordinal))
            return true;

        var baseTypeName = GetBaseTypeName(classDecl);
        return baseTypeName is "Controller" or "ControllerBase";
    }

    private static string GetBaseTypeName(ClassDeclarationSyntax classDecl)
    {
        if (classDecl.BaseList?.Types.Count > 0)
        {
            var firstType = classDecl.BaseList.Types[0].Type;
            return firstType switch
            {
                SimpleNameSyntax simple => simple.Identifier.Text,
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                _ => null
            } ?? "";
        }

        return "";
    }

    private static string DetectFramework(ClassDeclarationSyntax classDecl)
    {
        if (HasAttribute(classDecl, "ApiController"))
            return "AspNetCore";

        var baseTypeFull = GetBaseTypeFullName(classDecl);
        if (baseTypeFull.Contains("System.Web.Mvc", StringComparison.Ordinal))
            return "AspNetMvc";

        return "AspNetCore";
    }

    private static string GetBaseTypeFullName(ClassDeclarationSyntax classDecl)
    {
        if (classDecl.BaseList?.Types.Count > 0)
        {
            var firstType = classDecl.BaseList.Types[0].Type;
            return firstType.ToString();
        }

        return "";
    }

    private static bool IsAction(MethodDeclarationSyntax method, string className)
    {
        // 跳过 NonAction
        if (HasAttribute(method, "NonAction"))
            return false;

        // 跳过构造函数
        if (method.Identifier.Text == className)
            return false;

        // 跳过非 public 方法
        if (!IsPublic(method))
            return false;

        // 跳过 static
        if (method.Modifiers.Any(SyntaxKind.StaticKeyword))
            return false;

        // 跳过属性访问器 (get/set/add/remove)
        if (method.Identifier.Text is "get_" or "set_")
            return false;

        return true;
    }

    private static bool IsPublic(MethodDeclarationSyntax method)
    {
        var modifiers = method.Modifiers;
        if (modifiers.Count == 0)
            return true; // 默认 internal，但属性访问器等

        return modifiers.Any(SyntaxKind.PublicKeyword);
    }

    private static string GetClassRoute(ClassDeclarationSyntax classDecl, string className)
    {
        var routeAttr = classDecl.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => IsRouteAttribute(a));

        if (routeAttr is null)
            return "";

        var template = GetAttributeStringArg(routeAttr);
        return template is null ? "" : ReplaceTokens(template, className, null);
    }

    private static string GetActionRouteTemplate(MethodDeclarationSyntax method, string methodName)
    {
        // 从 HTTP 方法属性获取模板
        var httpAttr = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => HttpMethodAttributes.Contains(GetAttributeName(a)));

        if (httpAttr is not null)
        {
            var template = GetAttributeStringArg(httpAttr);
            if (template is not null)
                return template;
        }

        // Route 属性
        var routeAttr = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => IsRouteAttribute(a));

        if (routeAttr is not null)
        {
            var template = GetAttributeStringArg(routeAttr);
            if (template is not null)
                return template;
        }

        return "";
    }

    private static string GetHttpMethod(MethodDeclarationSyntax method, string methodName)
    {
        // 属性优先
        var attr = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Select(a => GetAttributeName(a))
            .FirstOrDefault(name => AttributeToHttpMethod.ContainsKey(name));

        if (attr is not null)
            return AttributeToHttpMethod[attr];

        // 方法名约定
        return methodName switch
        {
            _ when StartsWithWord(methodName, "Get") => "GET",
            _ when StartsWithWord(methodName, "Post")
                || StartsWithWord(methodName, "Create") => "POST",
            _ when StartsWithWord(methodName, "Put")
                || StartsWithWord(methodName, "Update") => "PUT",
            _ when StartsWithWord(methodName, "Delete")
                || StartsWithWord(methodName, "Remove") => "DELETE",
            _ when StartsWithWord(methodName, "Patch") => "PATCH",
            _ => "GET"
        };
    }

    private static string BuildFullRoute(
        string baseRoute,
        string actionTemplate,
        string className,
        string methodName)
    {
        if (string.IsNullOrEmpty(baseRoute) && string.IsNullOrEmpty(actionTemplate))
            return ConventionRoute(className, methodName);

        if (string.IsNullOrEmpty(actionTemplate))
            return string.IsNullOrEmpty(baseRoute)
                ? ConventionRoute(className, methodName)
                : baseRoute;

        var resolvedTemplate = ReplaceTokens(actionTemplate, className, methodName);

        if (string.IsNullOrEmpty(baseRoute))
            return resolvedTemplate;

        return $"{baseRoute.TrimEnd('/')}/{resolvedTemplate.TrimStart('/')}";
    }

    private static string ConventionRoute(string className, string methodName)
    {
        var controllerSegment = className.EndsWith("Controller", StringComparison.Ordinal)
            ? className[..^"Controller".Length]
            : className;

        return $"/{controllerSegment.ToLowerInvariant()}/{methodName.ToLowerInvariant()}";
    }

    private static string ReplaceTokens(string template, string className, string? methodName)
    {
        var controllerSegment = className.EndsWith("Controller", StringComparison.Ordinal)
            ? className[..^"Controller".Length]
            : className;

        var result = template
            .Replace("[controller]", controllerSegment.ToLowerInvariant(), StringComparison.Ordinal)
            .Replace("[controller]", controllerSegment, StringComparison.Ordinal);

        if (methodName is not null)
        {
            result = result
                .Replace("[action]", methodName.ToLowerInvariant(), StringComparison.Ordinal)
                .Replace("[action]", methodName, StringComparison.Ordinal);
        }

        return result;
    }

    private static bool IsRouteAttribute(AttributeSyntax attr)
    {
        var name = GetAttributeName(attr);
        return name is "Route" or "RouteAttribute";
    }

    private static string GetAttributeName(AttributeSyntax attr)
    {
        var name = attr.Name switch
        {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => ""
        };

        return name;
    }

    private static bool HasAttribute(MemberDeclarationSyntax declaration, string attributeName)
    {
        return declaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => GetAttributeName(a) == attributeName);
    }

    private static string? GetAttributeStringArg(AttributeSyntax attr)
    {
        if (attr.ArgumentList?.Arguments.Count > 0)
        {
            var firstArg = attr.ArgumentList.Arguments[0];
            var expr = firstArg.Expression;

            if (expr is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            return expr.ToString();
        }

        return null;
    }

    private static List<string> GetParameterTypes(MethodDeclarationSyntax method)
    {
        return method.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "")
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var parts = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(ns => ns.Name.ToString());

        return string.Join(".", parts);
    }

    private static string GetClassName(TypeDeclarationSyntax type)
    {
        var names = new List<string>();
        for (SyntaxNode? current = type; current is TypeDeclarationSyntax typeDecl; current = current.Parent)
            names.Insert(0, typeDecl.Identifier.Text);

        return string.Join(".", names);
    }

    private static bool StartsWithWord(string name, string prefix)
    {
        return name.StartsWith(prefix, StringComparison.Ordinal)
            && name.Length > prefix.Length
            && char.IsUpper(name[prefix.Length]);
    }
}

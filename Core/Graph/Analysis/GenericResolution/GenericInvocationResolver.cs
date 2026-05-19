// =============================================================================
// GenericResolution/GenericInvocationResolver.cs — 泛型方法调用解析器 (Roslyn)
// =============================================================================
// 使用 Roslyn InvocationExpressionSyntax + GenericNameSyntax 替代 regex。
// 从方法体内的调用表达式解析 Entity：
//   1. 检测 session.Query<T>() 等 NHibernate 泛型 API 调用
//   2. 检测 repo.Query(), dao.Get() 等 Repository 方法调用
//   3. 通过字段/变量类型追踪到具体的 Entity
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class GenericInvocationResolver
{
    private readonly GenericInheritanceMap _inheritanceMap;
    private readonly GenericTypeResolver _typeResolver;
    private readonly RepositoryPatternDetector _patternDetector;

    private static readonly HashSet<string> NhSessionMethods = new(StringComparer.Ordinal)
    {
        "Query", "Get", "Load", "Save", "Update", "Delete", "SaveOrUpdate",
        "CreateQuery", "CreateSQLQuery", "CreateCriteria",
        "QueryOver", "GetNamedQuery", "UniqueResult", "List", "Count"
    };

    private static readonly HashSet<string> RepositoryMethods = new(StringComparer.Ordinal)
    {
        "GetById", "FindById", "GetAll", "FindAll", "GetList", "FindBy",
        "GetBy", "QueryBy", "Save", "Update", "Delete", "Insert",
        "SearchBy", "CountBy", "ExistsBy",
        "GetEntity", "FindEntity", "QueryList", "QueryCount"
    };

    private static readonly HashSet<string> GenericApiMethods = new(StringComparer.Ordinal)
    {
        "Query", "Get", "Load", "Save", "Delete", "Update",
        "FindById", "GetById", "FindBy", "GetBy"
    };

    public GenericInvocationResolver(GenericInheritanceMap inheritanceMap)
    {
        _inheritanceMap = inheritanceMap;
        _typeResolver = new GenericTypeResolver(inheritanceMap);
        _patternDetector = new RepositoryPatternDetector();
    }

    public List<ResolvedInvocation> ResolveInvocations(
        string sourceContent,
        string filePath,
        string methodClassName,
        string projectPath)
    {
        var tree = CSharpSyntaxTree.ParseText(
            $"class _Wrapper {{ void _M() {{ {sourceContent} }} }}");
        var root = tree.GetCompilationUnitRoot();
        var methodDecl = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodDecl is null)
            return new List<ResolvedInvocation>();

        var classFieldTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        return ResolveInvocationsFromMethod(
            methodDecl, classFieldTypes, filePath, methodClassName, projectPath);
    }

    public List<ResolvedInvocation> ResolveInvocationsFromMethod(
        MethodDeclarationSyntax method,
        IReadOnlyDictionary<string, string> classFieldTypes,
        string filePath,
        string methodClassName,
        string projectPath)
    {
        var results = new List<ResolvedInvocation>();
        var localVariables = ExtractLocalVariableTypes(method);

        var typeEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in classFieldTypes)
            typeEnvironment[k] = v;
        foreach (var (k, v) in localVariables)
            typeEnvironment[k] = v;

        ExtractParameterTypes(method, typeEnvironment);

        foreach (var invoc in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            InvocationSyntaxInfo? invInfo = ParseInvocation(invoc);
            if (invInfo is null)
                continue;

            if (!string.IsNullOrEmpty(invInfo.GenericArg))
            {
                if (_typeResolver.ResolveEntityFromInvocation(
                        invInfo.Receiver, invInfo.Method, invInfo.GenericArg, methodClassName)
                    is { Count: > 0 } entities)
                {
                    foreach (var entity in entities)
                    {
                        results.Add(ResolvedInvocation.Create(
                            invInfo.FullExpression, entity.EntityClass,
                            entity.Confidence, entity.ResolutionType,
                            filePath, methodClassName));
                    }
                }
            }

            if (NhSessionMethods.Contains(invInfo.Method))
            {
                var receiverLower = invInfo.Receiver.ToLowerInvariant();
                var isSession = receiverLower.Contains("session")
                    || receiverLower.Contains("_session")
                    || receiverLower.EndsWith("session");

                if (isSession && !string.IsNullOrEmpty(invInfo.GenericArg))
                {
                    results.Add(ResolvedInvocation.Create(
                        invInfo.FullExpression, invInfo.GenericArg,
                        GenericResolutionConfidence.Exact, "session-generic",
                        filePath, methodClassName));
                }
            }

            if (RepositoryMethods.Contains(invInfo.Method) || GenericApiMethods.Contains(invInfo.Method))
            {
                var receiverType = ResolveReceiverType(invInfo.Receiver, typeEnvironment);

                if (!string.IsNullOrEmpty(receiverType))
                {
                    var entities = _typeResolver.ResolveEntityFromFieldType(receiverType, methodClassName);
                    foreach (var entity in entities.Take(3))
                    {
                        results.Add(ResolvedInvocation.Create(
                            invInfo.FullExpression, entity.EntityClass,
                            entity.Confidence, $"repo-receiver:{entity.ResolutionType}",
                            filePath, methodClassName));
                    }
                }
            }

            var patternResult = _patternDetector.Detect(methodClassName);
            if (patternResult?.EntityType is not null
                && (RepositoryMethods.Contains(invInfo.Method) || GenericApiMethods.Contains(invInfo.Method)))
            {
                results.Add(ResolvedInvocation.Create(
                    invInfo.FullExpression, patternResult.EntityType,
                    GenericResolutionConfidence.High, "class-pattern",
                    filePath, methodClassName));
            }
        }

        return results
            .DistinctBy(r => $"{r.Expression}|{r.EntityClass}", StringComparer.Ordinal)
            .ToList();
    }

    private static InvocationSyntaxInfo? ParseInvocation(InvocationExpressionSyntax invoc)
    {
        string receiver = "";
        string methodName;
        string genericArg = "";

        if (invoc.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = ExtractReceiverText(memberAccess.Expression);
            var name = memberAccess.Name;

            if (name is GenericNameSyntax genericName)
            {
                methodName = genericName.Identifier.Text;
                genericArg = genericName.TypeArgumentList.Arguments
                    .Select(a => a.ToString().Trim()).FirstOrDefault() ?? "";
            }
            else
            {
                methodName = name.Identifier.Text;
            }
        }
        else if (invoc.Expression is IdentifierNameSyntax identifierName)
        {
            methodName = identifierName.Identifier.Text;
        }
        else if (invoc.Expression is GenericNameSyntax directGeneric)
        {
            methodName = directGeneric.Identifier.Text;
            genericArg = directGeneric.TypeArgumentList.Arguments
                .Select(a => a.ToString().Trim()).FirstOrDefault() ?? "";
        }
        else
        {
            return null;
        }

        if (string.IsNullOrEmpty(methodName))
            return null;

        return new InvocationSyntaxInfo
        {
            Receiver = receiver,
            Method = methodName,
            GenericArg = genericArg,
            FullExpression = $"{receiver}.{methodName}<{genericArg}>"
        };
    }

    private static string ExtractReceiverText(ExpressionSyntax expr)
    {
        return expr switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax ma =>
                $"{ExtractReceiverText(ma.Expression)}.{ma.Name.Identifier.Text}",
            ThisExpressionSyntax => "this",
            InvocationExpressionSyntax inv =>
                ExtractReceiverText(inv.Expression) + "(...)",
            _ => expr.ToString()
        };
    }

    private static Dictionary<string, string> ExtractLocalVariableTypes(MethodDeclarationSyntax method)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);

        var bodyNode = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (bodyNode is null)
            return types;

        foreach (var localDecl in bodyNode.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>())
        {
            var typeSyntax = localDecl.Declaration.Type;
            var typeName = typeSyntax.ToString().Trim();

            foreach (var variable in localDecl.Declaration.Variables)
            {
                var varName = variable.Identifier.Text;

                if (!types.ContainsKey(varName))
                    types[varName] = CleanTypeNameForEnv(typeName);
            }
        }

        foreach (var varDecl in bodyNode.DescendantNodes()
            .OfType<VariableDeclarationSyntax>())
        {
            var typeSyntax = varDecl.Type;
            var typeName = typeSyntax.ToString().Trim();

            foreach (var variable in varDecl.Variables)
            {
                var varName = variable.Identifier.Text;
                if (!types.ContainsKey(varName) && char.IsUpper(typeName[0]))
                    types[varName] = CleanTypeNameForEnv(typeName);
            }
        }

        return types;
    }

    private static void ExtractParameterTypes(
        MethodDeclarationSyntax method,
        Dictionary<string, string> types)
    {
        foreach (var param in method.ParameterList.Parameters)
        {
            var typeName = param.Type?.ToString().Trim() ?? "";
            var paramName = param.Identifier.Text;

            if (!string.IsNullOrEmpty(typeName) && !types.ContainsKey(paramName)
                && char.IsUpper(typeName[0]))
            {
                types[paramName] = CleanTypeNameForEnv(typeName);
            }
        }
    }

    private static string ResolveReceiverType(
        string receiverName,
        IReadOnlyDictionary<string, string> typeEnvironment)
    {
        if (typeEnvironment.TryGetValue(receiverName, out var typeName))
            return typeName;

        return "";
    }

    private static string CleanTypeNameForEnv(string typeName) =>
        typeName.Replace("?", "").Trim();
}

internal sealed class InvocationSyntaxInfo
{
    public string Receiver { get; set; } = "";
    public string Method { get; set; } = "";
    public string GenericArg { get; set; } = "";
    public string FullExpression { get; set; } = "";
}

public sealed class ResolvedInvocation
{
    public string Expression { get; set; } = "";
    public string EntityClass { get; set; } = "";
    public GenericResolutionConfidence Confidence { get; set; }
    public string ResolutionMethod { get; set; } = "";
    public string? SourceFile { get; set; }
    public string? ContainingClass { get; set; }

    public static ResolvedInvocation Create(
        string expression,
        string entityClass,
        GenericResolutionConfidence confidence,
        string resolutionMethod,
        string? sourceFile = null,
        string? containingClass = null) => new()
        {
            Expression = expression,
            EntityClass = entityClass,
            Confidence = confidence,
            ResolutionMethod = resolutionMethod,
            SourceFile = sourceFile,
            ContainingClass = containingClass
        };
}

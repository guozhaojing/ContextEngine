using System.Text.RegularExpressions;
using System.Xml.Linq;
using Core.Graph.Identity;
using Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Graph.Analysis.Analyzers;

public sealed partial class NHibernateAnalyzer : IGraphAnalyzer
{
    public string Name => "nh-entity";

    private const string EdgeKindEntityAccess = "nh:entity-access";

    private const string ExternalNodePrefix = "ext::nh:entity";

    private static readonly HashSet<string> ExcludedDirNames =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    private static readonly HashSet<string> SessionApiMethods = new(StringComparer.Ordinal)
    {
        "Query", "Get", "Save", "Update", "Delete", "SaveOrUpdate",
        "CreateQuery", "CreateSQLQuery", "CreateCriteria"
    };

    public void Analyze(GraphAnalysisContext context)
    {
        var scanRoot = context.Scan.ScanRoot;
        var allUnits = context.Scan.AllCodeUnits;

        // ① Parse .hbm.xml Entity Mappings
        var entityMap = ParseHbmMappings(scanRoot);
        // 空 EntityMap 也继续——Session API 仍可能被识别 (回退命名约定)

        // ② Discover Session API calls from all CodeUnits' source files
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);
        var seenFacts = new HashSet<string>(StringComparer.Ordinal);

        var unitsByFile = context.UnitsByFile;

        foreach (var group in unitsByFile)
        {
            var fileUnits = group.ToList();
            if (fileUnits.Count == 0)
                continue;

            var absolutePath = fileUnits[0].FilePath;
            var relativePath = fileUnits[0].RelativeFilePath;

            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                continue;
            if (!context.Scope.ShouldAnalyzeFile(relativePath))
                continue;
            if (!processedFiles.Add(absolutePath))
                continue;

            var sourceText = File.ReadAllText(absolutePath);
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();

            var nsName = GetFileNamespace(root);
            var unitById = fileUnits.ToDictionary(u => u.Id, StringComparer.Ordinal);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var apiInfo = ClassifySessionApi(invocation);
                if (apiInfo is null)
                    continue;

                var callerMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (callerMethod is null)
                    continue;

                var callerClassName = GetContainingClassName(callerMethod);
                var callerNs = GetMethodNamespace(callerMethod, root);
                var callerMethodName = callerMethod.Identifier.Text;
                var callerParamTypes = GetParameterTypes(callerMethod);
                // Use the first unit's ProjectPath (API caller is always in the same project)
                var callerProjPath = fileUnits[0].ProjectPath;

                var callerId = MethodIdBuilder.FromMethod(
                    callerProjPath, callerNs, callerClassName, callerMethodName, callerParamTypes).Value;

                if (!context.NodesById.ContainsKey(callerId))
                    continue;

                var resolveResult = ResolveEntityClass(apiInfo, invocation, entityMap);
                var entityClass = resolveResult.EntityClass;
                var entityNamespace = ResolveEntityNamespace(entityClass, fileUnits, nsName);
                var table = ResolveTable(entityClass, entityMap);
                var confidence = DetermineConfidence(apiInfo, entityClass, entityMap, resolveResult.TraceConfidence);

                // --- Fact: entity-access ---
                var factKey = $"{callerId}|nh-entity-access|{entityClass}";
                if (seenFacts.Add(factKey))
                {
                    var data = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["api"] = apiInfo.ApiName,
                        ["entityClass"] = entityClass,
                        ["entityNamespace"] = entityNamespace,
                        ["table"] = table,
                        ["confidence"] = confidence.ToString().ToLowerInvariant()
                    };
                    if (entityClass.Length > 0 && entityMap.TryGetValue(entityClass, out var em))
                    {
                        data["mappingFile"] = em.SourceFile;
                        if (!string.IsNullOrEmpty(em.Schema))
                            data["schema"] = em.Schema;
                    }

                    context.AddFact(callerId, "nh-entity-access", GraphSubjectKinds.Method, relativePath, data);
                }

                // --- Annotation ---
                if (!string.IsNullOrEmpty(entityClass))
                {
                    context.AddAnnotation(callerId, "entity", entityClass, relativePath);
                    context.AddAnnotation(callerId, "table", table, relativePath);
                    context.AddAnnotation(callerId, "api", apiInfo.ApiName, relativePath);
                }

                // --- ExtraEdge: method → entity external node ---
                // Only produce edge when entity is determined with Medium+ confidence
                if (!string.IsNullOrEmpty(entityClass) && confidence >= ResolutionConfidence.Medium)
                {
                    var entityNodeId = BuildEntityNodeId(entityNamespace, entityClass, table);
                    var edgeKey = $"{callerId}→{entityNodeId}:{EdgeKindEntityAccess}";
                    if (seenEdges.Add(edgeKey))
                    {
                        context.AddExtraEdge(
                            fromId: callerId,
                            toId: entityNodeId,
                            kind: EdgeKindEntityAccess,
                            label: $"entity:{entityClass} → {table}",
                            isResolved: false,
                            sourceFile: relativePath,
                            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["api"] = apiInfo.ApiName,
                                ["entityClass"] = entityClass,
                                ["entityNamespace"] = entityNamespace,
                                ["table"] = table,
                        ["confidence"] = confidence.ToString().ToLowerInvariant()
                            });
                    }
                }

                // --- HQL / SQL Fact ---
                var queryString = ExtractQueryString(invocation, apiInfo.ApiName);
                if (queryString is not null)
                {
                    var hqlKey = $"{callerId}|hql|{queryString.GetHashCode()}";
                    if (seenFacts.Add(hqlKey))
                    {
                        var isHql = apiInfo.ApiName is "CreateQuery" or "CreateCriteria";
                        var queryData = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [isHql ? "hql" : "sql"] = queryString,
                            ["api"] = apiInfo.ApiName,
                            ["confidence"] = "exact"
                        };
                        if (!string.IsNullOrEmpty(entityClass))
                            queryData["entityClass"] = entityClass;

                        context.AddFact(callerId, isHql ? "nh-hql" : "nh-sql",
                            GraphSubjectKinds.Method, relativePath, queryData);
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HBM Mapping Parser
    // ═══════════════════════════════════════════════════════════════

    private static Dictionary<string, EntityMapping> ParseHbmMappings(string scanRoot)
    {
        var entityMap = new Dictionary<string, EntityMapping>(StringComparer.Ordinal);

        var hbmFiles = Directory.EnumerateFiles(scanRoot, "*.hbm.xml", SearchOption.AllDirectories)
            .Where(f => !IsUnderExcludedDirectory(f, scanRoot));

        foreach (var filePath in hbmFiles)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var mapping = doc.Root;
                if (mapping is null)
                    continue;

                if (!string.Equals(mapping.Name.LocalName, "hibernate-mapping", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var classElem in mapping.Elements())
                {
                    if (!string.Equals(classElem.Name.LocalName, "class", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var nameAttr = classElem.Attribute("name")?.Value;
                    var tableAttr = classElem.Attribute("table")?.Value;
                    var schemaAttr = classElem.Attribute("schema")?.Value;

                    if (string.IsNullOrWhiteSpace(nameAttr))
                        continue;

                    var (ns, className) = ParseTypeName(nameAttr);
                    if (string.IsNullOrEmpty(className))
                        continue;

                    var key = className;
                    entityMap[key] = new EntityMapping(
                        ns,
                        className,
                        tableAttr ?? InferTableName(className),
                        schemaAttr,
                        NormalizeFilePath(filePath, scanRoot));
                }
            }
            catch
            {
                // 跳过无效 XML
            }
        }

        return entityMap;
    }

    // ═══════════════════════════════════════════════════════════════
    // Session API Detection
    // ═══════════════════════════════════════════════════════════════

    private sealed record SessionApiInfo(string ApiName, string? GenericEntity, string? ArgEntity);

    private static SessionApiInfo? ClassifySessionApi(InvocationExpressionSyntax invocation)
    {
        var methodName = GetInvokedMethodName(invocation);
        if (string.IsNullOrEmpty(methodName) || !SessionApiMethods.Contains(methodName))
            return null;

        // 检查调用接收者是否为 session
        var receiver = GetInvocationReceiver(invocation);
        if (receiver is null)
            return null;

        if (!IsSessionVariable(receiver))
            return null;

        string? genericEntity = null;
        string? argEntity = null;

        switch (methodName)
        {
            case "Query":
            case "Get":
                genericEntity = ExtractGenericArgument(invocation);
                break;
            case "Save":
            case "Update":
            case "Delete":
            case "SaveOrUpdate":
                argEntity = ExtractFirstArgumentType(invocation);
                break;
            case "CreateQuery":
            case "CreateSQLQuery":
            case "CreateCriteria":
                // String-based API, entity from HQL or argument
                break;
        }

        return new SessionApiInfo(methodName, genericEntity, argEntity);
    }

    // ═══════════════════════════════════════════════════════════════
    // Entity Resolution
    // ═══════════════════════════════════════════════════════════════

    private sealed record EntityResolveResult(
        string EntityClass,
        ResolutionConfidence TraceConfidence);

    private static EntityResolveResult ResolveEntityClass(
        SessionApiInfo api,
        InvocationExpressionSyntax invocation,
        Dictionary<string, EntityMapping> entityMap)
    {
        // 1. 泛型参数 → 精确
        if (!string.IsNullOrEmpty(api.GenericEntity))
            return new(api.GenericEntity, ResolutionConfidence.Exact);

        // 2. 参数类型 → 从变量名推断
        if (!string.IsNullOrEmpty(api.ArgEntity))
            return new(api.ArgEntity, ResolutionConfidence.Medium);

        // 3. HQL 字面量 → extract directly
        var hql = ExtractQueryString(invocation, api.ApiName);
        if (!string.IsNullOrEmpty(hql))
        {
            var fromEntity = ExtractEntityFromHql(hql);
            if (fromEntity is not null)
                return new(fromEntity, ResolutionConfidence.High);
        }

        // 4. HQL 变量追溯
        var traced = TraceVariableToHql(invocation);
        if (traced is not null)
        {
            var fromEntity = ExtractEntityFromHql(traced.Hql);
            if (fromEntity is not null)
                return new(fromEntity, traced.Confidence);
        }

        return new("", ResolutionConfidence.Low);
    }

    private static ResolutionConfidence DetermineConfidence(
        SessionApiInfo api,
        string entityClass,
        Dictionary<string, EntityMapping> entityMap,
        ResolutionConfidence traceConfidence = ResolutionConfidence.Low)
    {
        if (string.IsNullOrEmpty(entityClass))
            return ResolutionConfidence.Low;

        var hasMapping = entityMap.ContainsKey(entityClass);
        var hasGeneric = !string.IsNullOrEmpty(api.GenericEntity);

        if (hasMapping && hasGeneric)
            return ResolutionConfidence.Exact;
        if (hasMapping)
            return ResolutionConfidence.High;
        if (hasGeneric)
            return ResolutionConfidence.Medium;

        if (traceConfidence > ResolutionConfidence.Low)
            return traceConfidence;

        return ResolutionConfidence.Low;
    }

    private static string ResolveEntityNamespace(
        string entityClass,
        List<CodeUnit> fileUnits,
        string fileNamespace)
    {
        if (string.IsNullOrEmpty(entityClass))
            return "";

        // 从文件 using 推导
        // 简化：使用文件自己的 namespace
        var candidate = fileUnits
            .Where(u => u.ClassName == entityClass)
            .Select(u => u.Namespace)
            .FirstOrDefault(ns => !string.IsNullOrEmpty(ns));

        return candidate ?? fileNamespace;
    }

    private static string ResolveTable(
        string entityClass,
        Dictionary<string, EntityMapping> entityMap)
    {
        if (string.IsNullOrEmpty(entityClass))
            return "";

        if (entityMap.TryGetValue(entityClass, out var mapping))
            return mapping.Table;

        return InferTableName(entityClass);
    }

    private static ResolutionConfidence DetermineConfidence(
        SessionApiInfo api,
        string entityClass,
        Dictionary<string, EntityMapping> entityMap)
    {
        if (string.IsNullOrEmpty(entityClass))
            return ResolutionConfidence.Low;

        var hasMapping = entityMap.ContainsKey(entityClass);
        var hasGeneric = !string.IsNullOrEmpty(api.GenericEntity);

        if (hasMapping && hasGeneric)
            return ResolutionConfidence.Exact;
        if (hasMapping)
            return ResolutionConfidence.High;
        if (hasGeneric)
            return ResolutionConfidence.Medium;

        return ResolutionConfidence.Low;
    }

    // ═══════════════════════════════════════════════════════════════
    // HQL / SQL Extraction
    // ═══════════════════════════════════════════════════════════════

    private static string? ExtractQueryString(InvocationExpressionSyntax invocation, string apiName)
    {
        if (apiName is not ("CreateQuery" or "CreateSQLQuery" or "CreateCriteria"))
            return null;

        var args = invocation.ArgumentList?.Arguments;
        if (args is null || args.Value.Count == 0)
            return null;

        var firstArg = args.Value[0].Expression;
        if (firstArg is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        // 非字面量 → 走变量追溯
        return null;
    }

    // ── HQL Entity Regex ──────────────────────────────────────────

    [GeneratedRegex(@"\bfrom\s+(\w+)(?:\s+\w+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FromEntityPattern();

    [GeneratedRegex(@"\bupdate\s+(\w+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdateEntityPattern();

    [GeneratedRegex(@"\bdelete\s+from\s+(\w+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeleteEntityPattern();

    [GeneratedRegex(@"\bjoin\s+(\w+)(?:\s+\w+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JoinEntityPattern();

    internal static string? ExtractEntityFromHql(string hql)
    {
        if (string.IsNullOrWhiteSpace(hql))
            return null;

        var match = DeleteEntityPattern().Match(hql);
        if (match.Success)
            return match.Groups[1].Value;

        match = UpdateEntityPattern().Match(hql);
        if (match.Success)
            return match.Groups[1].Value;

        match = FromEntityPattern().Match(hql);
        if (match.Success)
            return match.Groups[1].Value;

        match = JoinEntityPattern().Match(hql);
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    // ── HQL Variable Tracing ──────────────────────────────────────

    private sealed record TracedHql(string Hql, ResolutionConfidence Confidence);

    private static TracedHql? TraceVariableToHql(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList?.Arguments;
        if (args is null || args.Value.Count == 0)
            return null;

        var firstArg = args.Value[0].Expression;

        // 变量名: CreateQuery(hql)
        if (firstArg is IdentifierNameSyntax varName)
            return TraceVariableInMethod(varName.Identifier.Text, invocation);

        // 成员访问: CreateQuery(this._hql)
        if (firstArg is MemberAccessExpressionSyntax member
            && member.Expression is ThisExpressionSyntax)
            return TraceFieldInClass(member.Name.Identifier.Text, invocation);

        // 字符串插值: CreateQuery($"from {x} o")
        if (firstArg is InterpolatedStringExpressionSyntax interpolated)
            return TraceInterpolation(interpolated);

        // 二元拼接: CreateQuery("from " + entity + " o")
        if (firstArg is BinaryExpressionSyntax binary)
            return TraceConcatenation(binary);

        return null;
    }

    private static TracedHql? TraceVariableInMethod(string varName, InvocationExpressionSyntax invocation)
    {
        var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method?.Body is null)
            return null;

        // 在 CreateQuery 调用前的语句中查找变量赋值
        var preceding = method.Body.Statements
            .TakeWhile(s => s.SpanStart < invocation.SpanStart)
            .Reverse();

        foreach (var stmt in preceding)
        {
            var result = ExtractHqlFromStatement(stmt, varName);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static TracedHql? ExtractHqlFromStatement(StatementSyntax stmt, string varName)
    {
        // var hql = "from Order o ..."
        if (stmt is LocalDeclarationStatementSyntax local)
        {
            foreach (var decl in local.Declaration.Variables)
            {
                if (decl.Identifier.Text != varName)
                    continue;

                if (decl.Initializer is null)
                    continue;

                return ExtractHqlFromExpression(decl.Initializer.Value);
            }
        }

        // hql = "from Order o ..." (assignment to existing variable)
        if (stmt is ExpressionStatementSyntax exprStmt
            && exprStmt.Expression is AssignmentExpressionSyntax assign
            && assign.Left is IdentifierNameSyntax leftId
            && leftId.Identifier.Text == varName)
        {
            return ExtractHqlFromExpression(assign.Right);
        }

        return null;
    }

    private static TracedHql? ExtractHqlFromExpression(ExpressionSyntax expr)
    {
        // 字面量: "from Order o..."
        if (expr is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return new TracedHql(literal.Token.ValueText, ResolutionConfidence.High);
        }

        // 字符串插值: $"from {entity} o..."
        if (expr is InterpolatedStringExpressionSyntax interpolated)
            return TraceInterpolation(interpolated);

        // 字符串拼接: "from " + entity + " o..."
        if (expr is BinaryExpressionSyntax binary)
            return TraceConcatenation(binary);

        return null;
    }

    private static TracedHql? TraceInterpolation(InterpolatedStringExpressionSyntax interpolated)
    {
        // 重建 HQL 字符串
        var parts = new List<string>();
        var hasEntityVar = false;
        string? entityVarName = null;

        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                parts.Add(text.TextToken.Text);
            }
            else if (content is InterpolationSyntax interpolation)
            {
                var slotExpr = interpolation.Expression;
                if (slotExpr is IdentifierNameSyntax id)
                {
                    parts.Add(id.Identifier.Text);
                    if (id.Identifier.Text.Contains("Entity", StringComparison.OrdinalIgnoreCase)
                        || id.Identifier.Text.Contains("Class", StringComparison.OrdinalIgnoreCase))
                    {
                        hasEntityVar = true;
                        entityVarName = id.Identifier.Text;
                    }
                }
                else
                {
                    parts.Add(slotExpr.ToString());
                }
            }
        }

        var reconstructed = string.Concat(parts);
        return new TracedHql(reconstructed,
            hasEntityVar ? ResolutionConfidence.High : ResolutionConfidence.Medium);
    }

    private static TracedHql? TraceConcatenation(BinaryExpressionSyntax binary)
    {
        // 递归展平 a + b + c 拼接链
        var parts = new List<string>();
        FlattenConcatenation(binary, parts);
        var reconstructed = string.Concat(parts);
        return new TracedHql(reconstructed, ResolutionConfidence.Medium);
    }

    private static void FlattenConcatenation(ExpressionSyntax expr, List<string> parts)
    {
        if (expr is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            FlattenConcatenation(binary.Left, parts);
            FlattenConcatenation(binary.Right, parts);
        }
        else if (expr is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            parts.Add(literal.Token.ValueText);
        }
        else if (expr is IdentifierNameSyntax id)
        {
            parts.Add(id.Identifier.Text);
        }
        else if (expr is MemberAccessExpressionSyntax member)
        {
            parts.Add(member.ToString());
        }
        else
        {
            parts.Add(expr.ToString());
        }
    }

    private static TracedHql? TraceFieldInClass(string fieldName, InvocationExpressionSyntax invocation)
    {
        var classDecl = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is null)
            return null;

        foreach (var member in classDecl.Members)
        {
            if (member is FieldDeclarationSyntax field)
            {
                foreach (var decl in field.Declaration.Variables)
                {
                    if (decl.Identifier.Text != fieldName)
                        continue;

                    if (decl.Initializer is not null)
                        return ExtractHqlFromExpression(decl.Initializer.Value);
                }
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // External Entity Node
    // ═══════════════════════════════════════════════════════════════

    internal static string BuildEntityNodeId(string entityNamespace, string entityClass, string table)
    {
        var ns = string.IsNullOrEmpty(entityNamespace) ? "" : entityNamespace;
        var name = string.IsNullOrEmpty(entityClass) ? "unknown" : entityClass;
        var tbl = string.IsNullOrEmpty(table) ? "unknown" : table;
        return $"{ExternalNodePrefix}::{ns}.{name}::{tbl}";
    }

    // ═══════════════════════════════════════════════════════════════
    // SyntaxTree Helpers
    // ═══════════════════════════════════════════════════════════════

    private static string GetInvokedMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => ""
        };
    }

    private static string? GetInvocationReceiver(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax member
            && member.Expression is not ThisExpressionSyntax
            && member.Expression is not BaseExpressionSyntax)
        {
            return member.Expression.ToString();
        }

        return null;
    }

    private static bool IsSessionVariable(string receiver)
    {
        return receiver.Contains("session", StringComparison.OrdinalIgnoreCase)
            || receiver.Contains("Session", StringComparison.Ordinal)
            || receiver.Contains("_session", StringComparison.Ordinal);
    }

    private static string? ExtractGenericArgument(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax member
            && member.Name is GenericNameSyntax generic
            && generic.TypeArgumentList.Arguments.Count > 0)
        {
            var typeArg = generic.TypeArgumentList.Arguments[0];
            return typeArg switch
            {
                SimpleNameSyntax simple => simple.Identifier.Text,
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                _ => typeArg.ToString()
            };
        }

        // 直接泛型调用: Query<T>()
        if (invocation.Expression is GenericNameSyntax directGeneric
            && directGeneric.TypeArgumentList.Arguments.Count > 0)
        {
            return directGeneric.TypeArgumentList.Arguments[0].ToString();
        }

        return null;
    }

    private static string? ExtractFirstArgumentType(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList?.Arguments;
        if (args is null || args.Value.Count == 0)
            return null;

        var firstArg = args.Value[0].Expression;
        // 变量名 → 尝试推断类型
        if (firstArg is IdentifierNameSyntax id)
        {
            return id.Identifier.Text;
        }

        // new Order() → "Order"
        if (firstArg is ObjectCreationExpressionSyntax creation)
        {
            return creation.Type.ToString();
        }

        return null;
    }

    private static string GetContainingClassName(MethodDeclarationSyntax method)
    {
        var type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (type is null)
            return "";

        var names = new List<string>();
        for (SyntaxNode? current = type; current is TypeDeclarationSyntax t; current = current.Parent)
            names.Insert(0, t.Identifier.Text);

        return string.Join(".", names);
    }

    private static string GetMethodNamespace(MethodDeclarationSyntax method, SyntaxNode root)
    {
        var ns = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return ns?.Name.ToString() ?? "";
    }

    private static string GetFileNamespace(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .Select(ns => ns.Name.ToString())
            .FirstOrDefault() ?? "";
    }

    private static List<string> GetParameterTypes(MethodDeclarationSyntax method)
    {
        return method.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "")
            .Where(t => t.Length > 0)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // Type Name Parsing
    // ═══════════════════════════════════════════════════════════════

    private static (string Namespace, string ClassName) ParseTypeName(string typeFullName)
    {
        // "MyApp.Domain.Order, MyApp" → (Namespace, ClassName)
        var typePart = typeFullName.Split(',')[0].Trim();
        var lastDot = typePart.LastIndexOf('.');
        if (lastDot < 0)
            return ("", typePart);

        return (typePart[..lastDot], typePart[(lastDot + 1)..]);
    }

    private static string InferTableName(string className)
    {
        if (string.IsNullOrEmpty(className))
            return "unknown";

        // Order → Orders (简单复数)
        return className + "s";
    }

    // ═══════════════════════════════════════════════════════════════
    // Path Utilities
    // ═══════════════════════════════════════════════════════════════

    private static string NormalizeFilePath(string fullPath, string scanRoot) =>
        Path.GetRelativePath(scanRoot, fullPath).Replace('\\', '/');

    private static bool IsUnderExcludedDirectory(string filePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (ExcludedDirNames.Contains(segment))
                return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // Inner Types
    // ═══════════════════════════════════════════════════════════════

    internal sealed record EntityMapping(
        string Namespace,
        string ClassName,
        string Table,
        string? Schema,
        string SourceFile);
}

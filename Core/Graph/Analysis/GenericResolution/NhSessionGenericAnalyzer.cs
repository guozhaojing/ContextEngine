// =============================================================================
// GenericResolution/NhSessionGenericAnalyzer.cs — NHibernate 泛型解析分析器
// =============================================================================
// IGraphAnalyzer 实现：
//   1. 构建 GenericInheritanceMap 扫描所有 class 的泛型继承关系
//   2. 检测 Repository/DAO/Service 模式
//   3. 解析泛型方法调用中的 Entity 类型
//   4. 产出 nh:entity-access edges/facts/annotations
//   5. 无缝集成到 SemanticTraversalEngine
//
// 解析策略：
//   Exact:  显式泛型参数 (session.Query<EQA_Reagent>())
//   High:   继承链类型参数推导 (class ReagentRepo : BaseRepository<EQA_Reagent>)
//   Medium: Repository/DAO 命名约定 + 字段类型推导
//   Low:    方法名启发式匹配
// =============================================================================

using Core.Graph.Analysis;
using Core.Graph.Identity;
using Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class NhSessionGenericAnalyzer : IGraphAnalyzer
{
    public string Name => "nh-generic-resolution";

    private const string EdgeKindEntityAccess = "nh:entity-access";
    private const string ExternalNodePrefix = "ext::nh:entity";

    private static readonly HashSet<string> ExcludedDirNames =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    private GenericInheritanceMap _inheritanceMap = new();
    private GenericInvocationResolver _invocationResolver = null!;
    private GenericTypeResolver _typeResolver = null!;
    private EntityClassRegistry _entityRegistry = new();
    private DaoFieldDetector _daoFieldDetector = null!;
    private DaoCallSiteResolver _daoCallSiteResolver = null!;

    public GenericResolutionResult ResolutionResult { get; private set; } = new();

    public void Analyze(GraphAnalysisContext context)
    {
        ResolutionResult = new GenericResolutionResult
        {
            AnalyzerName = Name,
            ScanRoot = context.Scan.ScanRoot,
            GeneratedAt = DateTime.UtcNow
        };

        // Phase 1: 构建继承映射
        BuildInheritanceMap(context);

        _invocationResolver = new GenericInvocationResolver(_inheritanceMap);
        _typeResolver = new GenericTypeResolver(_inheritanceMap);

        ResolutionResult.ClassesScanned = _inheritanceMap.Count;

        // Phase 1.5: 构建 Entity ↔ Class 双向注册表 (BLL/DAO 模式)
        _entityRegistry = new EntityClassRegistry();
        _entityRegistry.Build(_inheritanceMap);
        _daoFieldDetector = new DaoFieldDetector(_entityRegistry, _inheritanceMap);
        _daoCallSiteResolver = new DaoCallSiteResolver(_entityRegistry);

        ResolutionResult.DiscoveredEntities = _entityRegistry.AllEntities.ToList();
        ResolutionResult.DiscoveredTables = ResolutionResult.DiscoveredEntities
            .Select(e => e + "s").ToList();

        foreach (var entityName in _entityRegistry.AllEntities)
        {
            var bllBindings = _entityRegistry.GetBllBindingsForEntity(entityName);
            foreach (var b in bllBindings)
                ResolutionResult.RecordEntityBinding(entityName, b.SourceFile, b.BindingPath);

            var daoBindings = _entityRegistry.GetDaoBindingsForEntity(entityName);
            foreach (var d in daoBindings)
                ResolutionResult.RecordEntityBinding(entityName, d.SourceFile, d.BindingPath);
        }

        // Phase 2: 检测 Repository 模式
        var patternMatches = new Dictionary<string, PatternMatchResult>(StringComparer.Ordinal);
        foreach (var (className, classInfo) in _inheritanceMap.Classes)
        {
            var patternResult = new RepositoryPatternDetector().Detect(className);
            if (patternResult is not null && patternResult.IsRepositoryPattern)
            {
                patternMatches[className] = patternResult;
            }
        }

        ResolutionResult.RepositoryClassesFound = patternMatches.Count;

        // Phase 3: Per-file 解析
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);
        var seenFacts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in context.UnitsByFile)
        {
            var fileUnits = group.ToList();
            if (fileUnits.Count == 0)
                continue;

            var filePath = fileUnits[0].FilePath;
            var relativePath = fileUnits[0].RelativeFilePath;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                continue;
            if (!context.Scope.ShouldAnalyzeFile(relativePath))
                continue;
            if (!processedFiles.Add(filePath))
                continue;

            AnalyzeFile(fileUnits, filePath, relativePath, context,
                patternMatches, seenEdges, seenFacts);
        }

        ResolutionResult.DiscoveredEntities = _entityRegistry.AllEntities.ToList();
        ResolutionResult.DiscoveredTables = ResolutionResult.DiscoveredEntities
            .Select(e => e + "s").ToList();
        ResolutionResult.TotalEntitiesDiscovered = ResolutionResult.DiscoveredEntities.Count;
        ResolutionResult.TotalTablesDiscovered = ResolutionResult.DiscoveredTables.Count;
    }

    private void BuildInheritanceMap(GraphAnalysisContext context)
    {
        _inheritanceMap = new GenericInheritanceMap();
        _inheritanceMap.Build(context.GetUnitsInScope());
    }

    private void AnalyzeFile(
        List<CodeUnit> fileUnits,
        string filePath,
        string relativePath,
        GraphAnalysisContext context,
        Dictionary<string, PatternMatchResult> patternMatches,
        HashSet<string> seenEdges,
        HashSet<string> seenFacts)
    {
        var sourceText = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var unitById = fileUnits.ToDictionary(u => u.Id, StringComparer.Ordinal);
        var projectPath = fileUnits[0].ProjectPath;

        // 建立类内字段类型映射（用于上下文推断）
        var classFieldTypes = BuildClassFieldTypeMap(root);

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var className = GetFullClassName(classDecl);
            var classNs = GetNamespace(classDecl);

            // 解析该 class 的泛型 entity binding
            var classEntities = ResolveClassEntityBindings(className, classNs, patternMatches);

            // ④ BLL/DAO 字段检测：BLL 类中查找 DAO 字段 → 推导 Entity
            var daoFields = _daoFieldDetector.DetectInClass(sourceText, relativePath, className);

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodName = method.Identifier.Text;
                var paramTypes = GetParameterTypes(method);
                var methodId = MethodIdBuilder.FromMethod(
                    projectPath, classNs, className, methodName, paramTypes).Value;

                if (!context.NodesById.ContainsKey(methodId))
                    continue;

                // ① Class-level entity resolution (from generic inheritance)
                foreach (var entity in classEntities)
                {
                    ProduceEntityAccess(
                        methodId, entity.EntityClass, "", entity.Confidence,
                        entity.ResolutionType, entity.ViaClass,
                        relativePath, context, seenEdges, seenFacts, methodName, "");
                }

                // ② 解析方法体内的泛型调用
                var methodContent = GetMethodContent(method, sourceText);
                if (!string.IsNullOrEmpty(methodContent))
                {
                    var invocations = _invocationResolver.ResolveInvocations(
                        methodContent, relativePath, className, projectPath);

                    foreach (var inv in invocations)
                    {
                        if (inv.Confidence >= GenericResolutionConfidence.Medium)
                        {
                            ProduceEntityAccess(
                                methodId, inv.EntityClass, "", inv.Confidence,
                                $"invocation:{inv.ResolutionMethod}", className,
                                relativePath, context, seenEdges, seenFacts,
                                methodName, inv.Expression);
                        }
                    }

                    ResolutionResult.TotalInvocationsResolved += invocations.Count;
                }

                // ③ 检测方法名模式 (如 GetReagentList → reagent entity)
                DetectMethodNamePattern(methodId, methodName, className,
                    relativePath, context, seenEdges, seenFacts, patternMatches);

                // ⑤ BLL→DAO 调用传播：方法体中调用 DAO 方法 → 推导 Entity
                if (daoFields.Count > 0 && methodContent is not null)
                {
                    var daoCallSites = _daoCallSiteResolver.Resolve(
                        methodContent, daoFields, className);

                    foreach (var callSite in daoCallSites)
                    {
                        if (callSite.Confidence >= GenericResolutionConfidence.Medium)
                        {
                            ProduceEntityAccess(
                                methodId, callSite.EntityName, "", callSite.Confidence,
                                $"dao-call:{callSite.DaoClassName}.{callSite.CalledMethod}",
                                className, relativePath, context,
                                seenEdges, seenFacts, methodName,
                                $"{callSite.DaoFieldName}.{callSite.CalledMethod}()");
                        }
                    }
                }
            }
        }
    }

    private List<EntityResolution> ResolveClassEntityBindings(
        string className,
        string classNs,
        Dictionary<string, PatternMatchResult> patternMatches)
    {
        var results = new List<EntityResolution>();

        // 从继承映射解析
        var classInfo = _inheritanceMap.FindClass(className, classNs);
        if (classInfo is not null)
        {
            var resolved = _typeResolver.ResolveEntityFromClass(className, classNs);
            results.AddRange(resolved);
        }

        // 从 pattern 匹配提取 entity name
        PatternMatchResult? matchedPattern = null;
        if (patternMatches.TryGetValue(className, out var exactPattern))
        {
            matchedPattern = exactPattern;
        }
        else
        {
            matchedPattern = patternMatches.Values.FirstOrDefault(p =>
                p.SimpleName == className || p.ClassName == className);
        }

        if (matchedPattern is not null && matchedPattern.EntityType is not null)
        {
            results.Add(new EntityResolution
            {
                EntityClass = matchedPattern.EntityType,
                ResolutionType = $"pattern:{matchedPattern.PatternType}",
                Confidence = GenericResolutionConfidence.Medium,
                ViaClass = className
            });
        }

        return results;
    }

    private void DetectMethodNamePattern(
        string methodId,
        string methodName,
        string className,
        string relativePath,
        GraphAnalysisContext context,
        HashSet<string> seenEdges,
        HashSet<string> seenFacts,
        Dictionary<string, PatternMatchResult> patternMatches)
    {
        // 如果方法名中包含已知的 entity 名，添加匹配
        foreach (var (_, classInfo) in _inheritanceMap.Classes)
        {
            if (classInfo.TypeParameters.Count == 0)
                continue;

            var entityName = classInfo.Name;
            if (entityName.Length < 3)
                continue;

            var methodLower = methodName.ToLowerInvariant();
            var entityLower = entityName.ToLowerInvariant();

            // GetXxx / FindXxx / SaveXxx 等模式
            if (methodLower.StartsWith("get", StringComparison.Ordinal)
                || methodLower.StartsWith("find", StringComparison.Ordinal)
                || methodLower.StartsWith("save", StringComparison.Ordinal)
                || methodLower.StartsWith("delete", StringComparison.Ordinal)
                || methodLower.StartsWith("update", StringComparison.Ordinal))
            {
                var prefixLen = methodLower.StartsWith("get", StringComparison.Ordinal) ? 3 :
                    methodLower.StartsWith("find", StringComparison.Ordinal) ? 4 :
                    methodLower.StartsWith("save", StringComparison.Ordinal) ? 4 :
                    methodLower.StartsWith("delete", StringComparison.Ordinal) ? 6 : 6;

                var suffix = methodLower[prefixLen..];
                if (suffix.Contains(entityLower))
                {
                    ProduceEntityAccess(
                        methodId, entityName, "", GenericResolutionConfidence.Low,
                        "method-name-heuristic", className,
                        relativePath, context, seenEdges, seenFacts, methodName, "");
                }
            }
        }

        // 检查类的 pattern → 方法可能是 entity 操作
        if (patternMatches.TryGetValue(className, out var classPattern)
            && classPattern.EntityType is not null)
        {
            var accessVerbs = new[] { "Get", "Find", "Query", "Save", "Delete", "Update",
                "Insert", "Search", "Count", "Exists", "Load", "Create" };

            foreach (var verb in accessVerbs)
            {
                if (methodName.StartsWith(verb, StringComparison.Ordinal) && methodName.Length > verb.Length)
                {
                    ProduceEntityAccess(
                        methodId, classPattern.EntityType, "",
                        GenericResolutionConfidence.Medium,
                        $"repo-method:{verb}", className,
                        relativePath, context, seenEdges, seenFacts, methodName, "");
                    break;
                }
            }
        }
    }

    private void ProduceEntityAccess(
        string methodId,
        string entityClass,
        string entityNamespace,
        GenericResolutionConfidence confidence,
        string resolutionMethod,
        string viaClass,
        string relativePath,
        GraphAnalysisContext context,
        HashSet<string> seenEdges,
        HashSet<string> seenFacts,
        string methodName,
        string expression)
    {
        if (string.IsNullOrEmpty(entityClass))
            return;

        var table = InferTableName(entityClass);
        var entityNodeId = BuildEntityNodeId(entityNamespace, entityClass, table);
        var stdConfidence = confidence.ToStandardConfidence();

        // Fact
        var factKey = $"{methodId}|generic:{entityClass}|{resolutionMethod}";
        if (seenFacts.Add(factKey))
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["api"] = "generic",
                ["entityClass"] = entityClass,
                ["entityNamespace"] = entityNamespace,
                ["table"] = table,
                ["confidence"] = stdConfidence.ToString().ToLowerInvariant(),
                ["viaClass"] = viaClass,
                ["resolution"] = resolutionMethod,
                ["generic:resolved"] = "true"
            };

            if (!string.IsNullOrEmpty(expression))
                data["expression"] = expression;

            context.AddFact(methodId, "nh-entity-access",
                GraphSubjectKinds.Method, relativePath, data);
            ResolutionResult.FactsProduced++;
        }

        // Annotation
        context.AddAnnotation(methodId, "generic:resolved", entityClass, relativePath);
        context.AddAnnotation(methodId, "entity", entityClass, relativePath);
        context.AddAnnotation(methodId, "table", table, relativePath);
        context.AddAnnotation(methodId, "api", "generic", relativePath);
        ResolutionResult.AnnotationsProduced++;

        // ExtraEdge
        if (confidence >= GenericResolutionConfidence.Medium)
        {
            var edgeKey = $"{methodId}→{entityNodeId}:{EdgeKindEntityAccess}";
            if (seenEdges.Add(edgeKey))
            {
                var edgeLabel = $"generic:{entityClass} → {table} (via {viaClass})";
                var edgeAttrs = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["api"] = "generic",
                    ["entityClass"] = entityClass,
                    ["entityNamespace"] = entityNamespace,
                    ["table"] = table,
                    ["confidence"] = stdConfidence.ToString().ToLowerInvariant(),
                    ["viaClass"] = viaClass,
                    ["resolution"] = resolutionMethod,
                    ["generic:resolved"] = "true"
                };

                context.AddExtraEdge(
                    fromId: methodId,
                    toId: entityNodeId,
                    kind: EdgeKindEntityAccess,
                    label: edgeLabel,
                    isResolved: false,
                    sourceFile: relativePath,
                    attributes: edgeAttrs);
                ResolutionResult.EdgesProduced++;
            }
        }

        // 记录到结果
        ResolutionResult.Record(methodId, entityClass, entityNamespace,
            table, confidence, resolutionMethod, viaClass, relativePath);
    }

    private Dictionary<string, string> BuildClassFieldTypeMap(SyntaxNode root)
    {
        var fieldTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            foreach (var member in classDecl.Members)
            {
                if (member is FieldDeclarationSyntax field)
                {
                    var typeName = field.Declaration.Type.ToString();
                    foreach (var variable in field.Declaration.Variables)
                    {
                        fieldTypes[variable.Identifier.Text] = typeName;
                    }
                }
                else if (member is PropertyDeclarationSyntax property)
                {
                    var typeName = property.Type.ToString();
                    fieldTypes[property.Identifier.Text] = typeName;
                }
            }
        }

        return fieldTypes;
    }

    private static string GetMethodContent(MethodDeclarationSyntax method, string sourceText)
    {
        if (method.Body is not null)
            return method.Body.ToString();

        if (method.ExpressionBody is not null)
            return method.ExpressionBody.ToString();

        // Fallback: get from source text span
        return sourceText.Substring(method.Span.Start, method.Span.Length);
    }

    private static string GetFullClassName(ClassDeclarationSyntax classDecl)
    {
        var names = new List<string>();
        for (SyntaxNode? current = classDecl;
            current is TypeDeclarationSyntax typeDecl;
            current = current.Parent)
            names.Insert(0, typeDecl.Identifier.Text);

        return string.Join(".", names);
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var parts = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(ns => ns.Name.ToString());
        return string.Join(".", parts);
    }

    private static List<string> GetParameterTypes(MethodDeclarationSyntax method)
    {
        return method.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "")
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static string BuildEntityNodeId(string entityNamespace, string entityClass, string table)
    {
        var ns = string.IsNullOrEmpty(entityNamespace) ? "" : entityNamespace;
        var name = string.IsNullOrEmpty(entityClass) ? "unknown" : entityClass;
        var tbl = string.IsNullOrEmpty(table) ? "unknown" : table;
        return $"{ExternalNodePrefix}::{ns}.{name}::{tbl}";
    }

    private static string InferTableName(string className)
    {
        if (string.IsNullOrEmpty(className))
            return "unknown";

        return className + "s";
    }
}

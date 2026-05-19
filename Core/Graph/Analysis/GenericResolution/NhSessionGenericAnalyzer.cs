// =============================================================================
// GenericResolution/NhSessionGenericAnalyzer.cs — NHibernate 泛型解析分析器 (Roslyn)
// =============================================================================
// IGraphAnalyzer 实现：
//   1. 构建 GenericInheritanceMap 扫描所有 class 的泛型继承关系 (Roslyn SyntaxTree)
//   2. 检测 Repository/DAO/Service 模式
//   3. 解析泛型方法调用中的 Entity 类型 (Roslyn InvocationExpressionSyntax)
//   4. 产出 nh:entity-access edges/facts/annotations
//   5. 无缝集成到 SemanticTraversalEngine
//   6. 所有子解析器统一使用 Roslyn，无 regex 依赖
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
using Core.Semantics;
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

        BuildInheritanceMap(context);

        _invocationResolver = new GenericInvocationResolver(_inheritanceMap);
        _typeResolver = new GenericTypeResolver(_inheritanceMap);

        ResolutionResult.ClassesScanned = _inheritanceMap.Count;

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

        var patternMatches = new Dictionary<string, PatternMatchResult>(StringComparer.Ordinal);
        foreach (var (className, _) in _inheritanceMap.Classes)
        {
            var patternResult = new RepositoryPatternDetector().Detect(className);
            if (patternResult is not null && patternResult.IsRepositoryPattern)
                patternMatches[className] = patternResult;
        }

        ResolutionResult.RepositoryClassesFound = patternMatches.Count;

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

        var registryEntities = _entityRegistry.AllEntities;
        Console.WriteLine($"  [Registry] Found {registryEntities.Count} entities from explicit generic bindings");

        ResolutionResult.DiscoveredEntities = registryEntities.ToList();
        ResolutionResult.DiscoveredTables = ResolutionResult.DiscoveredEntities
            .Select(e => e + "s").ToList();
        ResolutionResult.TotalEntitiesDiscovered = ResolutionResult.DiscoveredEntities.Count;
        ResolutionResult.TotalTablesDiscovered = ResolutionResult.DiscoveredTables.Count;

        RunDiagnostics(context);
    }

    private void BuildInheritanceMap(GraphAnalysisContext context)
    {
        _inheritanceMap = new GenericInheritanceMap();

        var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectCount = 0;
        var totalCsFiles = 0;

        foreach (var project in context.Scan.Projects)
        {
            try
            {
                var absPath = Path.Combine(context.Scan.ScanRoot, project.ProjectPath);
                string? projectDir;

                if (File.Exists(absPath))
                    projectDir = Path.GetDirectoryName(absPath);
                else if (Directory.Exists(absPath))
                    projectDir = absPath;
                else
                    continue;

                if (projectDir is null || !Directory.Exists(projectDir))
                    continue;

                var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories);
                foreach (var f in csFiles)
                    allFiles.Add(f);

                projectCount++;
                totalCsFiles += csFiles.Length;
            }
            catch { }
        }

        foreach (var unit in context.GetUnitsInScope())
            allFiles.Add(unit.FilePath);

        Console.WriteLine($"  [InheritanceMap] Projects: {projectCount}, .cs files on disk: {totalCsFiles}, unique: {allFiles.Count}");

        _inheritanceMap.BuildFromFiles(allFiles);
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
        var root = tree.GetCompilationUnitRoot();

        var unitById = fileUnits.ToDictionary(u => u.Id, StringComparer.Ordinal);
        var projectPath = fileUnits[0].ProjectPath;

        var classFieldTypes = BuildClassFieldTypeMap(root);

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var className = GetFullClassName(classDecl);
            var classNs = GetNamespace(classDecl);

            var classEntities = ResolveClassEntityBindings(className, classNs, patternMatches);

            var perClassFieldTypes = BuildPerClassFieldTypeMap(classDecl);
            var mergedFieldTypes = new Dictionary<string, string>(classFieldTypes, StringComparer.Ordinal);
            foreach (var (k, v) in perClassFieldTypes)
                mergedFieldTypes[k] = v;

            var daoFields = _daoFieldDetector.DetectFromClassDeclaration(classDecl);

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodName = method.Identifier.Text;
                var paramTypes = GetParameterTypes(method);
                var methodId = MethodIdBuilder.FromMethod(
                    projectPath, classNs, className, methodName, paramTypes).Value;

                if (!context.NodesById.ContainsKey(methodId))
                    continue;

                foreach (var entity in classEntities)
                {
                    ProduceEntityAccess(
                        methodId, entity.EntityClass, "", entity.Confidence,
                        entity.ResolutionType, entity.ViaClass,
                        relativePath, context, seenEdges, seenFacts, methodName, "");
                }

                var invocations = _invocationResolver.ResolveInvocationsFromMethod(
                    method, mergedFieldTypes, relativePath, className, projectPath);

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

                DetectMethodNamePattern(methodId, methodName, className,
                    relativePath, context, seenEdges, seenFacts, patternMatches);

                if (daoFields.Count > 0)
                {
                    var methodBody = (SyntaxNode?)method.Body ?? method.ExpressionBody;
                    if (methodBody is not null)
                    {
                        var daoCallSites = _daoCallSiteResolver.Resolve(
                            methodBody, daoFields, className);

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
    }

    private List<EntityResolution> ResolveClassEntityBindings(
        string className,
        string classNs,
        Dictionary<string, PatternMatchResult> patternMatches)
    {
        var results = new List<EntityResolution>();

        var classInfo = _inheritanceMap.FindClass(className, classNs);
        if (classInfo is not null)
        {
            var resolved = _typeResolver.ResolveEntityFromClass(className, classNs);
            results.AddRange(resolved);
        }

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
        foreach (var (_, classInfo) in _inheritanceMap.Classes)
        {
            if (classInfo.TypeParameters.Count == 0)
                continue;

            var entityName = classInfo.Name;
            if (entityName.Length < 3)
                continue;

            var methodLower = methodName.ToLowerInvariant();
            var entityLower = entityName.ToLowerInvariant();

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

        context.AddAnnotation(methodId, "generic:resolved", entityClass, relativePath);
        context.AddAnnotation(methodId, "entity", entityClass, relativePath);
        context.AddAnnotation(methodId, "table", table, relativePath);
        context.AddAnnotation(methodId, "api", "generic", relativePath);
        ResolutionResult.AnnotationsProduced++;

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
                        fieldTypes[variable.Identifier.Text] = typeName;
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

    private static Dictionary<string, string> BuildPerClassFieldTypeMap(ClassDeclarationSyntax classDecl)
    {
        var fieldTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var member in classDecl.Members)
        {
            if (member is FieldDeclarationSyntax field)
            {
                var typeName = field.Declaration.Type.ToString();
                foreach (var variable in field.Declaration.Variables)
                    fieldTypes[variable.Identifier.Text] = typeName;
            }
            else if (member is PropertyDeclarationSyntax property)
            {
                var typeName = property.Type.ToString();
                fieldTypes[property.Identifier.Text] = typeName;
            }
        }

        return fieldTypes;
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

    private void RunDiagnostics(GraphAnalysisContext context)
    {
        var diagnostics = new List<GenericDiagnostic>();

        diagnostics.AddRange(DetectDuplicateEntitySources());
        diagnostics.AddRange(DetectAmbiguousGenericBindings());
        diagnostics.AddRange(DetectOrphanPropagationEdges(context));

        ResolutionResult.Diagnostics = diagnostics;
    }

    private List<GenericDiagnostic> DetectDuplicateEntitySources()
    {
        var diags = new List<GenericDiagnostic>();
        var entitySources = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var binding in _entityRegistry.ClassToBinding.Values)
        {
            if (!entitySources.ContainsKey(binding.EntityType))
                entitySources[binding.EntityType] = new List<string>();
            entitySources[binding.EntityType].Add($"{binding.ClassName} ({binding.BindingPath})");
        }

        foreach (var (entity, sources) in entitySources)
        {
            if (sources.Count > 1)
            {
                diags.Add(new GenericDiagnostic
                {
                    Severity = DiagnosticSeverity.Info,
                    Category = "duplicate-entity-source",
                    Message = $"Entity '{entity}' has {sources.Count} class bindings: {string.Join(", ", sources)}",
                    EntityClass = entity
                });
            }
        }

        return diags;
    }

    private List<GenericDiagnostic> DetectAmbiguousGenericBindings()
    {
        var diags = new List<GenericDiagnostic>();

        foreach (var (fullName, classInfo) in _inheritanceMap.Classes)
        {
            foreach (var baseType in classInfo.BaseTypes)
            {
                if (!baseType.IsGeneric || baseType.TypeArguments.Count == 0)
                    continue;

                for (var i = 0; i < baseType.TypeArguments.Count; i++)
                {
                    var arg = baseType.TypeArguments[i];
                    if (IsGenericParameter(arg))
                    {
                        var concrete = _inheritanceMap.ResolveConcreteType(classInfo, arg);
                        if (concrete is null)
                        {
                            var bindings = _inheritanceMap.ResolveTypeParameter(classInfo, arg);
                            if (bindings.Count == 0)
                            {
                                diags.Add(new GenericDiagnostic
                                {
                                    Severity = DiagnosticSeverity.Warning,
                                    Category = "unresolved-generic-binding",
                                    Message = $"Unresolved generic parameter '{arg}' in class '{fullName}' via base type '{baseType.FullName}'",
                                    EntityClass = fullName,
                                    ContextClass = baseType.FullName
                                });
                            }
                            else if (bindings.Count > 1)
                            {
                                diags.Add(new GenericDiagnostic
                                {
                                    Severity = DiagnosticSeverity.Warning,
                                    Category = "ambiguous-generic-binding",
                                    Message = $"Ambiguous binding for '{arg}' in '{fullName}': found {bindings.Count} possible bindings",
                                    EntityClass = fullName,
                                    ContextClass = baseType.FullName
                                });
                            }
                        }
                    }
                }
            }
        }

        return diags;
    }

    private List<GenericDiagnostic> DetectOrphanPropagationEdges(GraphAnalysisContext context)
    {
        var diags = new List<GenericDiagnostic>();

        foreach (var edge in context.Result.ExtraEdges)
        {
            if (edge.Kind == EdgeKindEntityAccess)
            {
                if (string.IsNullOrEmpty(edge.FromId) || string.IsNullOrEmpty(edge.ToId))
                {
                    diags.Add(new GenericDiagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Category = "orphan-propagation-edge",
                        Message = $"Orphan edge from '{edge.FromId}' to '{edge.ToId}': missing source or target",
                        EntityClass = edge.FromId,
                        ContextClass = edge.ToId
                    });
                }
            }
        }

        return diags;
    }

    private static bool IsGenericParameter(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;
        if (typeName.Length == 1 && char.IsUpper(typeName[0]))
            return true;
        if (typeName.StartsWith("T", StringComparison.Ordinal) && typeName.Length <= 2)
            return true;
        if (NamePatterns.IsGenericParameter(typeName))
            return true;
        return false;
    }
}

// =============================================================================
// Graph/CodeGraphBuilder.cs — 基础调用图构建器
// =============================================================================
// v4: 语法回退 + 同class连接改为纯字符串匹配，极快。
// =============================================================================

using Core.Graph.Building;
using Core.Graph.Identity;
using Core.Graph.Indexing;
using Core.Models;
using Core.Scanning;
using Core.Semantics;

namespace Core.Graph;

public static class CodeGraphBuilder
{
    public static CodeGraphBuildResult Build(SolutionScanResult scan)
    {
        var units = scan.AllCodeUnits;
        var graph = new CodeGraph { ScanRoot = scan.ScanRoot };
        var registry = new MethodRegistry(units);
        var targetResolver = new SemanticCallTargetResolver(registry);
        var nodeMap = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

        foreach (var unit in units)
            AddNode(graph, nodeMap, unit);

        foreach (var unit in units)
            AddEdgesForUnit(graph, nodeMap, targetResolver, unit);

        AddSyntaxFallbackEdges(graph, nodeMap, units);
        ForceConnectClassMethods(graph, nodeMap, units);
        ClassifyEdgeDependencyTypes(graph, nodeMap);

        EnsureExternalNodes(graph, nodeMap);
        GraphAdjacencyMaterializer.Apply(graph);
        var index = GraphIndex.Build(graph);

        var symbolBuilder = new SymbolGraphBuilder();
        var symbolIndex = symbolBuilder.EnrichGraph(graph, scan);

        return new CodeGraphBuildResult { Graph = graph, Index = index, SymbolIndex = symbolIndex };
    }

    private static void AddNode(CodeGraph graph, Dictionary<string, GraphNode> nodeMap, CodeUnit unit)
    {
        var id = MethodIdBuilder.FromCodeUnit(unit).Value;
        if (nodeMap.ContainsKey(id)) return;

        var label = unit.ParameterTypes.Count > 0
            ? $"{unit.ClassName}.{unit.MethodName}({string.Join(", ", unit.ParameterTypes)})"
            : $"{unit.ClassName}.{unit.MethodName}";

        graph.Nodes.Add(new GraphNode
        {
            Id = id, Kind = GraphNodeKind.Method, Label = label,
            ProjectName = unit.ProjectName, ProjectPath = unit.ProjectPath,
            Namespace = unit.Namespace, ClassName = unit.ClassName,
            MethodName = unit.MethodName, ParameterTypes = unit.ParameterTypes.ToList(),
            IsExternal = false, SourceFile = unit.FilePath,
            GroundingKind = GroundingKindKinds.SyntaxOnly,
        });
        nodeMap[id] = graph.Nodes[^1];
    }

    private static void AddEdgesForUnit(CodeGraph graph, Dictionary<string, GraphNode> nodeMap,
        SemanticCallTargetResolver targetResolver, CodeUnit unit)
    {
        var fromId = MethodIdBuilder.FromCodeUnit(unit).Value;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resolved in unit.ResolvedCalls)
        {
            if (!targetResolver.TryResolveTargetId(resolved, unit.ProjectPath, out var targetId)) continue;
            if (!seenTargets.Add($"{fromId}->{targetId}")) continue;

            var isResolved = nodeMap.ContainsKey(targetId) && !nodeMap[targetId].IsExternal;
            graph.Edges.Add(new GraphEdge
            {
                FromId = fromId, ToId = targetId,
                Call = ResolvedMethodInfoFormatter.ToQualifiedName(resolved),
                IsResolved = isResolved, Kind = GraphEdgeKinds.Call,
                Source = EdgeSourceKinds.Roslyn,
                Confidence = isResolved ? EdgeConfidenceKinds.High : EdgeConfidenceKinds.Medium,
                Evidence = resolved.IsExternal ? EdgeEvidenceKinds.SyntaxDirect : EdgeEvidenceKinds.SemanticInferred,
                PropagationDepth = 0, Grounded = isResolved,
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 语法回退边 v4: 纯字符串匹配，不用 Roslyn 解析，最多 5000 条边
    // ═══════════════════════════════════════════════════════════════

    private static void AddSyntaxFallbackEdges(CodeGraph graph, Dictionary<string, GraphNode> nodeMap, IReadOnlyList<CodeUnit> units)
    {
        const int maxEdges = 5000;
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in graph.Edges) seenEdges.Add($"{e.FromId}→{e.ToId}");

        var nameIdx = new Dictionary<string, List<(string Id, string Cls, string Proj)>>(StringComparer.Ordinal);
        foreach (var n in nodeMap.Values)
        {
            if (!nameIdx.TryGetValue(n.MethodName, out var l))
            { l = new(); nameIdx[n.MethodName] = l; }
            l.Add((n.Id, n.ClassName, n.ProjectPath));
        }

        foreach (var u in units)
        {
            if (graph.Edges.Count >= maxEdges) break;
            var fid = MethodIdBuilder.FromCodeUnit(u).Value;
            var body = u.Content;
            if (string.IsNullOrEmpty(body)) continue;

            foreach (var (mname, matches) in nameIdx)
            {
                if (graph.Edges.Count >= maxEdges) break;
                if (!body.Contains($"{mname}(", StringComparison.Ordinal)) continue;

                foreach (var (mid, mcls, mproj) in matches)
                {
                    if (graph.Edges.Count >= maxEdges) break;
                    var ek = $"{fid}→{mid}";
                    if (!seenEdges.Add(ek)) continue;
                    var same = string.Equals(u.ClassName, mcls, StringComparison.Ordinal)
                            && string.Equals(u.ProjectPath, mproj, StringComparison.Ordinal);
                    graph.Edges.Add(new GraphEdge
                    {
                        FromId = fid, ToId = mid, Call = mname, IsResolved = true,
                        Kind = GraphEdgeKinds.Call, Source = EdgeSourceKinds.Heuristic,
                        Confidence = same ? EdgeConfidenceKinds.Medium : EdgeConfidenceKinds.Low,
                        Evidence = same ? EdgeEvidenceKinds.SyntaxDirect : EdgeEvidenceKinds.SyntaxPattern,
                        PropagationDepth = 0, Grounded = true,
                    });
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 同class方法连接 v4: 字符串匹配，最多3000条边，跳过超大class
    // ═══════════════════════════════════════════════════════════════

    private static void ForceConnectClassMethods(CodeGraph graph, Dictionary<string, GraphNode> nodeMap, IReadOnlyList<CodeUnit> units)
    {
        const int maxEdges = 3000;
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in graph.Edges) seenEdges.Add($"{e.FromId}→{e.ToId}");

        foreach (var cg in units.GroupBy(u => $"{u.ProjectPath}|{u.ClassName}", StringComparer.Ordinal))
        {
            if (graph.Edges.Count >= maxEdges) break;
            var cu = cg.OrderBy(u => u.Id, StringComparer.Ordinal).ToList();
            if (cu.Count < 2 || cu.Count > 100) continue;

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in cu) names.Add(u.MethodName);

            foreach (var caller in cu)
            {
                if (graph.Edges.Count >= maxEdges) break;
                var body = caller.Content;
                if (string.IsNullOrEmpty(body)) continue;

                foreach (var mn in names)
                {
                    if (graph.Edges.Count >= maxEdges) break;
                    if (mn == caller.MethodName) continue;
                    if (!body.Contains($"{mn}(", StringComparison.Ordinal)) continue;

                    foreach (var tgt in cu)
                    {
                        if (graph.Edges.Count >= maxEdges) break;
                        if (tgt.MethodName != mn || tgt.Id == caller.Id) continue;
                        var ek = $"{caller.Id}→{tgt.Id}";
                        if (!seenEdges.Add(ek)) continue;
                        graph.Edges.Add(new GraphEdge
                        {
                            FromId = caller.Id, ToId = tgt.Id, Call = mn, IsResolved = true,
                            Kind = GraphEdgeKinds.Call, Source = EdgeSourceKinds.Heuristic,
                            Confidence = EdgeConfidenceKinds.Medium,
                            Evidence = EdgeEvidenceKinds.SyntaxDirect,
                            PropagationDepth = 0, Grounded = true,
                        });
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 边类型分类
    // ═══════════════════════════════════════════════════════════════

    private static void ClassifyEdgeDependencyTypes(CodeGraph graph, Dictionary<string, GraphNode> nodeMap)
    {
        foreach (var edge in graph.Edges)
        {
            nodeMap.TryGetValue(edge.FromId, out var fn);
            nodeMap.TryGetValue(edge.ToId, out var tn);
            var sameClass = fn != null && tn != null
                && string.Equals(fn.ClassName, tn.ClassName, StringComparison.Ordinal)
                && string.Equals(fn.ProjectPath, tn.ProjectPath, StringComparison.Ordinal);
            var isIfContract = edge.Kind == "spring:property-inj" || edge.Kind == "spring:object-get" || edge.Kind == "spring:implements";

            edge.DependencyType = isIfContract ? DependencyEdgeTypes.InterfaceContract
                : sameClass ? DependencyEdgeTypes.PrivateImplementation
                : edge.Evidence == EdgeEvidenceKinds.SemanticDirect ? DependencyEdgeTypes.DirectCall
                : DependencyEdgeTypes.TransitiveCall;

            edge.Attributes["dependencyType"] = edge.DependencyType;
        }
    }

    private static void EnsureExternalNodes(CodeGraph graph, Dictionary<string, GraphNode> nodeMap)
    {
        foreach (var edge in graph.Edges)
        {
            if (nodeMap.ContainsKey(edge.ToId)) continue;
            var n = new GraphNode
            {
                Id = edge.ToId, Kind = GraphNodeKind.External, Label = edge.Call,
                ClassName = "(external)", MethodName = edge.Call, IsExternal = true,
                GroundingKind = GroundingKindKinds.External,
                TruthType = TruthTypeKinds.Inferred, Confidence = 0.5,
            };
            graph.Nodes.Add(n);
            nodeMap[edge.ToId] = n;
        }
    }
}

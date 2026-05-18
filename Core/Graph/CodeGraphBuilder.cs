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

        EnsureExternalNodes(graph, nodeMap);
        GraphAdjacencyMaterializer.Apply(graph);
        var index = GraphIndex.Build(graph);

        return new CodeGraphBuildResult
        {
            Graph = graph,
            Index = index
        };
    }

    [Obsolete("Use MethodIdBuilder.FromCodeUnit instead.")]
    public static string ToNodeId(CodeUnit unit) => MethodIdBuilder.FromCodeUnit(unit).Value;

    private static void AddNode(
        CodeGraph graph,
        Dictionary<string, GraphNode> nodeMap,
        CodeUnit unit)
    {
        var node = new GraphNode
        {
            Id = MethodIdBuilder.FromCodeUnit(unit).Value,
            Label = $"{unit.ClassName}.{unit.MethodName}",
            ProjectName = unit.ProjectName,
            ProjectPath = unit.ProjectPath,
            Namespace = unit.Namespace,
            ClassName = unit.ClassName,
            MethodName = unit.MethodName,
            IsExternal = false
        };

        graph.Nodes.Add(node);
        nodeMap[node.Id] = node;
    }

    private static void AddEdgesForUnit(
        CodeGraph graph,
        Dictionary<string, GraphNode> nodeMap,
        SemanticCallTargetResolver targetResolver,
        CodeUnit unit)
    {
        var fromId = MethodIdBuilder.FromCodeUnit(unit).Value;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resolved in unit.ResolvedCalls)
        {
            if (!targetResolver.TryResolveTargetId(resolved, unit.ProjectPath, out var targetId))
                continue;

            if (!seenTargets.Add($"{fromId}->{targetId}"))
                continue;

            var isResolved = nodeMap.ContainsKey(targetId) && !nodeMap[targetId].IsExternal;
            graph.Edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = targetId,
                Call = ResolvedMethodInfoFormatter.ToQualifiedName(resolved),
                IsResolved = isResolved,
                Kind = GraphEdgeKinds.Call
            });
        }
    }

    private static void EnsureExternalNodes(CodeGraph graph, Dictionary<string, GraphNode> nodeMap)
    {
        foreach (var edge in graph.Edges)
        {
            if (nodeMap.ContainsKey(edge.ToId))
                continue;

            var externalNode = new GraphNode
            {
                Id = edge.ToId,
                Label = edge.Call,
                ClassName = "(external)",
                MethodName = edge.Call,
                IsExternal = true
            };

            graph.Nodes.Add(externalNode);
            nodeMap[edge.ToId] = externalNode;
        }
    }
}

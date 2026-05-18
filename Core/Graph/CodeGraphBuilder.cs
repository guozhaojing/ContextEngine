using Core.Models;
using Core.Scanning;

namespace Core.Graph;

public static class CodeGraphBuilder
{
    public static CodeGraph Build(SolutionScanResult scan)
    {
        var units = scan.AllCodeUnits;
        var graph = new CodeGraph { ScanRoot = scan.ScanRoot };
        var index = new MethodIndex(units);
        var nodeMap = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

        foreach (var unit in units)
        {
            var node = ToGraphNode(unit);
            graph.Nodes.Add(node);
            nodeMap[node.Id] = node;
        }

        foreach (var unit in units)
        {
            var fromId = ToNodeId(unit);
            var seenCalls = new HashSet<string>(StringComparer.Ordinal);

            foreach (var call in unit.Calls)
            {
                if (!seenCalls.Add(call))
                    continue;

                var targetId = CallResolver.ResolveTargetNodeId(call, unit, index);
                if (targetId is not null && nodeMap.ContainsKey(targetId))
                {
                    graph.Edges.Add(new GraphEdge
                    {
                        FromId = fromId,
                        ToId = targetId,
                        Call = call,
                        IsResolved = true
                    });
                    continue;
                }

                var externalId = ToExternalNodeId(call);
                if (!nodeMap.TryGetValue(externalId, out var externalNode))
                {
                    externalNode = new GraphNode
                    {
                        Id = externalId,
                        Label = call,
                        ClassName = "(external)",
                        MethodName = call,
                        IsExternal = true
                    };
                    graph.Nodes.Add(externalNode);
                    nodeMap[externalId] = externalNode;
                }

                graph.Edges.Add(new GraphEdge
                {
                    FromId = fromId,
                    ToId = externalId,
                    Call = call,
                    IsResolved = false
                });
            }
        }

        BuildReverseRelations(graph);
        return graph;
    }

    private static void BuildReverseRelations(CodeGraph graph)
    {
        var calledByMap = graph.Nodes.ToDictionary(
            node => node.Id,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            if (!calledByMap.TryGetValue(edge.ToId, out var callers))
                continue;

            if (!callers.Contains(edge.FromId, StringComparer.Ordinal))
                callers.Add(edge.FromId);
        }

        foreach (var node in graph.Nodes)
            node.CalledBy = calledByMap[node.Id];
    }

    public static string ToNodeId(CodeUnit unit) =>
        string.IsNullOrEmpty(unit.Namespace)
            ? $"{unit.ProjectName}|{unit.ClassName}.{unit.MethodName}"
            : $"{unit.ProjectName}|{unit.Namespace}.{unit.ClassName}.{unit.MethodName}";

    private static GraphNode ToGraphNode(CodeUnit unit) => new()
    {
        Id = ToNodeId(unit),
        Label = $"{unit.ClassName}.{unit.MethodName}",
        ProjectName = unit.ProjectName,
        Namespace = unit.Namespace,
        ClassName = unit.ClassName,
        MethodName = unit.MethodName,
        IsExternal = false
    };

    private static string ToExternalNodeId(string call) => $"external|{call}";
}

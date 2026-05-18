using Core.Graph;

namespace Core.Graph.Indexing;

/// <summary>
/// 根据边集物化 GraphNode.CalledBy（纯数据结构填充，无查询逻辑）。
/// </summary>
public static class GraphAdjacencyMaterializer
{
    public static void Apply(CodeGraph graph)
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
}

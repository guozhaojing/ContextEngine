// =============================================================================
// Graph/Indexing/GraphAdjacencyMaterializer.cs
// =============================================================================
// 根据 Edges 填充每个节点的 CalledBy 列表（反向邻接）。
// 属于构建后处理，不是查询逻辑。
// =============================================================================

using Core.Graph;

namespace Core.Graph.Indexing;

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

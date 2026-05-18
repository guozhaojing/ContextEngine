// =============================================================================
// Graph/Indexing/GraphIndex.cs — 邻接表索引（供查询层只读使用）
// =============================================================================
// GraphQueryService 只依赖本类，不直接遍历 CodeGraph.Edges。
// =============================================================================

using Core.Graph;

namespace Core.Graph.Indexing;

public sealed class GraphIndex
{
    private GraphIndex(
        IReadOnlyDictionary<string, GraphNode> nodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> callers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> callees)
    {
        Nodes = nodes;
        Callers = callers;
        Callees = callees;
    }

    public IReadOnlyDictionary<string, GraphNode> Nodes { get; }

    /// <summary>methodId → 调用它的方法 Id 列表。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Callers { get; }

    /// <summary>methodId → 它调用的方法 Id 列表。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Callees { get; }

    public static GraphIndex Build(CodeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var callerLists = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var calleeLists = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            if (calleeLists.TryGetValue(edge.FromId, out var outList)
                && !outList.Contains(edge.ToId, StringComparer.Ordinal))
                outList.Add(edge.ToId);

            if (callerLists.TryGetValue(edge.ToId, out var inList)
                && !inList.Contains(edge.FromId, StringComparer.Ordinal))
                inList.Add(edge.FromId);
        }

        var frozenCallers = callerLists.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);

        var frozenCallees = calleeLists.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);

        return new GraphIndex(nodes, frozenCallers, frozenCallees);
    }
}

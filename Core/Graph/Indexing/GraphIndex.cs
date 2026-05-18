using Core.Graph;

namespace Core.Graph.Indexing;

/// <summary>
/// 内存邻接索引：查询层唯一依赖的图遍历数据结构。
/// </summary>
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

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Callers { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Callees { get; }

    public static GraphIndex Build(CodeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var callers = nodes.Keys.ToDictionary(id => id, _ => (IReadOnlyList<string>)[], StringComparer.Ordinal);
        var callees = nodes.Keys.ToDictionary(id => id, _ => (IReadOnlyList<string>)[], StringComparer.Ordinal);

        var callerLists = callers.ToDictionary(pair => pair.Key, _ => new List<string>(), StringComparer.Ordinal);
        var calleeLists = callees.ToDictionary(pair => pair.Key, _ => new List<string>(), StringComparer.Ordinal);

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

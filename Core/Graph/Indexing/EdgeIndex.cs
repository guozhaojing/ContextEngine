using Core.Graph;

namespace Core.Graph.Indexing;

/// <summary>
/// 按边 Kind + 方向索引 — 语义遍历的快速路径。
/// 不做全量邻接，仅做 Kind 粒度过滤。
/// </summary>
public sealed class EdgeIndex
{
    private EdgeIndex(
        IReadOnlyDictionary<string, IReadOnlyList<EdgeInfo>> outgoingByKind,
        IReadOnlyDictionary<string, IReadOnlyList<EdgeInfo>> incomingByKind)
    {
        OutgoingByKind = outgoingByKind;
        IncomingByKind = incomingByKind;
    }

    /// <summary>methodId → 从该节点出发、按 Kind 分组的边列表。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EdgeInfo>> OutgoingByKind { get; }

    /// <summary>methodId → 指向该节点、按 Kind 分组的边列表。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EdgeInfo>> IncomingByKind { get; }

    public static EdgeIndex Build(CodeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var outgoing = new Dictionary<string, List<EdgeInfo>>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, List<EdgeInfo>>(StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            // Outgoing: from → edges
            if (!outgoing.TryGetValue(edge.FromId, out var outList))
            {
                outList = new List<EdgeInfo>();
                outgoing[edge.FromId] = outList;
            }

            outList.Add(new EdgeInfo
            {
                ToId = edge.ToId,
                Kind = edge.Kind,
                Label = edge.Call,
                IsResolved = edge.IsResolved,
                Attributes = edge.Attributes,
                Source = edge.Source,
                Confidence = edge.Confidence,
                Evidence = edge.Evidence,
                PropagationDepth = edge.PropagationDepth,
                Grounded = edge.Grounded,
            });

            // Incoming: to ← edges
            if (!incoming.TryGetValue(edge.ToId, out var inList))
            {
                inList = new List<EdgeInfo>();
                incoming[edge.ToId] = inList;
            }

            inList.Add(new EdgeInfo
            {
                ToId = edge.FromId,    // Incoming 方向: ToId = 边来源
                Kind = edge.Kind,
                Label = edge.Call,
                IsResolved = edge.IsResolved,
                Attributes = edge.Attributes,
                Source = edge.Source,
                Confidence = edge.Confidence,
                Evidence = edge.Evidence,
                PropagationDepth = edge.PropagationDepth,
                Grounded = edge.Grounded,
            });
        }

        // 冻结
        var frozenOut = outgoing.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EdgeInfo>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);

        var frozenIn = incoming.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EdgeInfo>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);

        return new EdgeIndex(frozenOut, frozenIn);
    }
}

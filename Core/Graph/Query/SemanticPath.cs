namespace Core.Graph.Query;

/// <summary>
/// 语义路径 — 从起点到终点的完整多跳路径。
/// </summary>
public sealed class SemanticPath
{
    /// <summary>路径指纹: nodeId-hop1-hop2-...</summary>
    public string PathId { get; init; } = "";

    /// <summary>路径上的节点 Id 序列。</summary>
    public IReadOnlyList<string> NodeIds { get; init; } = Array.Empty<string>();

    /// <summary>每跳的边 Kind。</summary>
    public IReadOnlyList<string> EdgeKinds { get; init; } = Array.Empty<string>();

    /// <summary>每跳的标签。</summary>
    public IReadOnlyList<string> HopLabels { get; init; } = Array.Empty<string>();

    /// <summary>人类可读摘要。</summary>
    public string Summary { get; init; } = "";

    /// <summary>跳数。</summary>
    public int Length => NodeIds.Count - 1;

    /// <summary>起点 Id。</summary>
    public string RootId => NodeIds.Count > 0 ? NodeIds[0] : "";

    /// <summary>终点 Id。</summary>
    public string LeafId => NodeIds.Count > 0 ? NodeIds[NodeIds.Count - 1] : "";
}

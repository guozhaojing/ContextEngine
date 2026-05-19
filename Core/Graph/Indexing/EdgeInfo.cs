namespace Core.Graph.Indexing;

/// <summary>
/// 只读边视图 — 仅供 EdgeIndex 存储，不暴露 GraphEdge 可变字段。
/// v2: 新增 Source / Confidence / Evidence / PropagationDepth / Grounded。
/// </summary>
public readonly struct EdgeInfo
{
    public string ToId { get; init; }

    public string Kind { get; init; }

    public string Label { get; init; }

    public bool IsResolved { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; }

    // ── v2: Truth fields ──

    public string Source { get; init; }

    public string Confidence { get; init; }

    public string Evidence { get; init; }

    public int PropagationDepth { get; init; }

    public bool Grounded { get; init; }

    public string GetAttr(string key) =>
        Attributes.TryGetValue(key, out var value) ? value : "";
}

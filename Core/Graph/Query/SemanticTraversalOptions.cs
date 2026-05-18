using Core.Graph.Analysis;

namespace Core.Graph.Query;

/// <summary>
/// 语义遍历方向。
/// </summary>
public enum TraversalDirection
{
    Forward,
    Backward,
    Both
}

/// <summary>
/// 语义遍历选项 — 控制 Layer-aware BFS 行为。
/// </summary>
public sealed class SemanticTraversalOptions
{
    /// <summary>允许的边 Kind 集合。null = 全量。</summary>
    public IReadOnlySet<string>? EdgeKinds { get; init; }

    /// <summary>允许访问的节点 Kind 集合。null = 全量。</summary>
    public IReadOnlySet<string>? NodeKinds { get; init; }

    /// <summary>遍历方向。</summary>
    public TraversalDirection Direction { get; init; } = TraversalDirection.Forward;

    /// <summary>最低置信度阈值。null = 不过滤。</summary>
    public ResolutionConfidence? MinConfidence { get; init; }

    /// <summary>最大跳数。null = 无限制。</summary>
    public int? MaxDepth { get; init; }

    /// <summary>命中此属性时停止（如 "aspnet-route:entry-point"）。</summary>
    public string? TargetAttributeKey { get; init; }

    public string? TargetAttributeValue { get; init; }

    /// <summary>最大产出路径数。</summary>
    public int MaxPaths { get; init; } = 200;

    /// <summary>是否去重路径。</summary>
    public bool DeduplicatePaths { get; init; } = true;

    /// <summary>是否包含 traced 信息。</summary>
    public bool IncludeEvidence { get; init; }

    public static SemanticTraversalOptions RouteToTable(string tableName) => new()
    {
        EdgeKinds = new HashSet<string>(StringComparer.Ordinal)
            { "call", "nh:entity-access" },
        Direction = TraversalDirection.Forward,
        MaxDepth = 15,
        DeduplicatePaths = true
    };

    public static SemanticTraversalOptions TableImpact(string tableName) => new()
    {
        EdgeKinds = new HashSet<string>(StringComparer.Ordinal)
            { "call", "nh:entity-access" },
        Direction = TraversalDirection.Backward,
        MaxDepth = 15,
        TargetAttributeKey = "aspnet-route:entry-point"
    };
}

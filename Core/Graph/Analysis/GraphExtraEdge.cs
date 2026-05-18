// =============================================================================
// Graph/Analysis/GraphExtraEdge.cs — 分析器产生的额外边（非 Roslyn call 边）
// =============================================================================

namespace Core.Graph.Analysis;

public sealed class GraphExtraEdge
{
    public string FromId { get; set; } = "";

    public string ToId { get; set; } = "";

    public string Kind { get; set; } = GraphEdgeKinds.Call;

    public string Label { get; set; } = "";

    public bool IsResolved { get; set; }

    public string? SourceFile { get; set; }

    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);
}

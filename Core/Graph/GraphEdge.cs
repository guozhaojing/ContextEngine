// =============================================================================
// Graph/GraphEdge.cs — 图边（一次调用关系 A → B）
// =============================================================================

namespace Core.Graph;

public class GraphEdge
{
    public string FromId { get; set; } = "";

    public string ToId { get; set; } = "";

    /// <summary>边上显示的调用文本（限定名）。</summary>
    public string Call { get; set; } = "";

    /// <summary>目标是否解析到解决方案内部方法。</summary>
    public bool IsResolved { get; set; }

    /// <summary>边类型：call（默认）、将来 route / ef / event 等。</summary>
    public string Kind { get; set; } = GraphEdgeKinds.Call;

    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);
}

public static class GraphEdgeKinds
{
    public const string Call = "call";
}

public static class EdgeLayer
{
    public const string Call = "call";

    public const string Framework = "framework";

    public const string Data = "data";

    public const string Transaction = "transaction";
}

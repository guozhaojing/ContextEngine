namespace Core.Graph;

public class GraphEdge
{
    public string FromId { get; set; } = "";

    public string ToId { get; set; } = "";

    public string Call { get; set; } = "";

    public bool IsResolved { get; set; }

    /// <summary>边类型，默认 call；后续可扩展 route / ef / event 等。</summary>
    public string Kind { get; set; } = GraphEdgeKinds.Call;

    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);
}

public static class GraphEdgeKinds
{
    public const string Call = "call";
}

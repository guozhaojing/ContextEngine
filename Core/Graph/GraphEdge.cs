namespace Core.Graph;

public class GraphEdge
{
    public string FromId { get; set; } = "";

    public string ToId { get; set; } = "";

    public string Call { get; set; } = "";

    public bool IsResolved { get; set; }
}

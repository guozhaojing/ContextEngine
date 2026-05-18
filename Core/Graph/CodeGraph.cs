namespace Core.Graph;

public class CodeGraph
{
    public string ScanRoot { get; set; } = "";

    public List<GraphNode> Nodes { get; set; } = new();

    public List<GraphEdge> Edges { get; set; } = new();

    public int ResolvedEdgeCount => Edges.Count(e => e.IsResolved);

    public int ExternalNodeCount => Nodes.Count(n => n.IsExternal);
}

namespace Core.Export.Dtos;

public sealed class NodesExport
{
    public int SchemaVersion => 1;
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public int NodeCount => Nodes.Count;
    public required IReadOnlyList<ExportNode> Nodes { get; init; }
    public ProjectionMode Mode { get; init; } = ProjectionMode.Detailed;
}

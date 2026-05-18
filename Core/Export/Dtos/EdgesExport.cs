namespace Core.Export.Dtos;

public sealed class EdgesExport
{
    public int SchemaVersion => 1;
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public int EdgeCount => Edges.Count;
    public required IReadOnlyList<ExportEdge> Edges { get; init; }
    public ProjectionMode Mode { get; init; } = ProjectionMode.Detailed;
}

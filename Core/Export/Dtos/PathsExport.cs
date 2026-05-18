namespace Core.Export.Dtos;

public sealed class PathsExport
{
    public int SchemaVersion => 1;
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public int PathCount => Paths.Count;
    public required IReadOnlyList<ExportPath> Paths { get; init; }
    public int TotalHops { get; init; }
    public ProjectionMode Mode { get; init; } = ProjectionMode.Visualization;
}

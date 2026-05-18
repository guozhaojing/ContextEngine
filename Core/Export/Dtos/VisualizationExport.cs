namespace Core.Export.Dtos;

public sealed class VisualizationExport
{
    public int SchemaVersion => 1;
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public required string GraphName { get; init; }
    public IReadOnlyList<ExportView> Views { get; init; } = Array.Empty<ExportView>();
    public LayerLayout? Layout { get; init; }
    public IReadOnlyDictionary<string, StyleHint>? StyleHints { get; init; }
}

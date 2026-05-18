namespace Core.Export.Dtos;

public sealed class ExportPath
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<string> Edges { get; init; }
    public required string Summary { get; init; }

    public IReadOnlyList<PathExplanationStep> Explanation { get; init; } = Array.Empty<PathExplanationStep>();

    public int Length { get; init; }
    public string? RootId { get; init; }
    public string? LeafId { get; init; }
    public string? RootLayer { get; init; }
    public string? LeafLayer { get; init; }
}

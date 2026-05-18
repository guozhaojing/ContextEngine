namespace Core.Export.Dtos;

public sealed class ExportView
{
    public required string ViewId { get; init; }
    public required string ViewName { get; init; }
    public string? Description { get; init; }
    public string? Direction { get; init; }
    public IReadOnlyList<string> EdgeKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RootNodeIds { get; init; } = Array.Empty<string>();
    public int? MaxDepth { get; init; }
    public string? CenterNodeId { get; init; }
    public int? Radius { get; init; }
}

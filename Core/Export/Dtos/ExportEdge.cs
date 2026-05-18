namespace Core.Export.Dtos;

public sealed class ExportEdge
{
    public required string Id { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Kind { get; init; }

    public string? Layer { get; init; }
    public string? Confidence { get; init; }
    public string? Label { get; init; }

    public int Sequence { get; init; }
}

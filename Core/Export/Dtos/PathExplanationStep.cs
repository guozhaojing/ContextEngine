namespace Core.Export.Dtos;

public sealed class PathExplanationStep
{
    public required string NodeId { get; init; }
    public required string Layer { get; init; }
    public required string HumanText { get; init; }
}

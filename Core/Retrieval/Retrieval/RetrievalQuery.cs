namespace Core.Retrieval.Retrieval;

public sealed class RetrievalQuery
{
    public required string Query { get; init; }
    public int TopK { get; init; } = 10;
    public IReadOnlyList<string>? PreferredLayers { get; init; }
    public IReadOnlyList<string>? PreferredEntities { get; init; }
    public IReadOnlyList<string>? PreferredTables { get; init; }
    public bool ExpandPaths { get; init; } = true;
    public double MinConfidence { get; init; } = 0.3;
}

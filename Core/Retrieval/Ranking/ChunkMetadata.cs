namespace Core.Retrieval.Ranking;

public sealed class ChunkMetadata
{
    public int CallerCount { get; init; }
    public int CalleeCount { get; init; }
    public int FanIn { get; init; }
    public int FanOut { get; init; }
    public int EntryPointDistance { get; init; }
    public int DataAccessDistance { get; init; }
    public double CentralityScore { get; init; }
    public double BusinessScore { get; init; }
    public int LayerDepth { get; init; }
    public int DependencyDepth { get; init; }
    public bool IsEntryPoint { get; init; }
    public bool IsEntityAccess { get; init; }
    public bool IsCrossProject { get; init; }
    public IReadOnlyList<string> RelatedTables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RelatedEntities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RelatedRoutes { get; init; } = Array.Empty<string>();
    public double ConfidenceScore { get; init; }
}

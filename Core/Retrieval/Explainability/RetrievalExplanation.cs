namespace Core.Retrieval.Explainability;

public sealed class RetrievalExplanation
{
    public required string ChunkId { get; init; }
    public required string ChunkTitle { get; init; }
    public required ScoreBreakdown Scores { get; init; }

    public IReadOnlyList<string> MatchedKeywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SharedEntities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SharedTables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SharedRoutes { get; init; } = Array.Empty<string>();
    public int EntryPointDistance { get; init; }
    public int DataAccessDistance { get; init; }
    public bool IsEntryPoint { get; init; }
    public bool IsEntityAccess { get; init; }
    public string Summary { get; init; } = "";
}

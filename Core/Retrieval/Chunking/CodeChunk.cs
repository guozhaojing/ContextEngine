namespace Core.Retrieval.Chunking;

public sealed class CodeChunk
{
    public required string ChunkId { get; init; }
    public ChunkKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Content { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NodeIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EdgeKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EntryPoints { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EntityNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TableNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RoutePatterns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();

    public double ImportanceScore { get; init; }
    public int TokenEstimate { get; init; }

    public Ranking.ChunkMetadata? Metadata { get; init; }
}

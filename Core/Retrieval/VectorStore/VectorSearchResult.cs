namespace Core.Retrieval.VectorStore;

public sealed class VectorSearchResult
{
    public required string ChunkId { get; init; }
    public double Similarity { get; init; }
    public int Rank { get; init; }
}

namespace Core.Retrieval.Embedding;

public sealed class ChunkEmbedding
{
    public required string ChunkId { get; init; }
    public string EmbeddingModel { get; init; } = "";
    public int Dimensions { get; init; }
    public required float[] Vector { get; init; }
    public int TokenCount { get; init; }
    public string CreatedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public required string ContentHash { get; init; }
}

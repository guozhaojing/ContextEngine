using Core.Retrieval.Embedding;

namespace Core.Retrieval.VectorStore;

public interface IVectorStore
{
    int Count { get; }
    void Index(IEnumerable<ChunkEmbedding> embeddings);
    IReadOnlyList<VectorSearchResult> Search(float[] queryVector, int topK = 10);
    void Clear();
}

using Core.Retrieval.Embedding;

namespace Core.Retrieval.VectorStore;

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly List<(string chunkId, float[] vector)> _store = new();

    public int Count => _store.Count;

    public void Index(IEnumerable<ChunkEmbedding> embeddings)
    {
        foreach (var e in embeddings)
            _store.Add((e.ChunkId, e.Vector));
    }

    public IReadOnlyList<VectorSearchResult> Search(float[] queryVector, int topK = 10)
    {
        if (_store.Count == 0) return Array.Empty<VectorSearchResult>();

        var results = new List<VectorSearchResult>(_store.Count);

        for (var i = 0; i < _store.Count; i++)
        {
            var (chunkId, vector) = _store[i];
            var sim = CosineSimilarity(queryVector, vector);
            results.Add(new VectorSearchResult
            {
                ChunkId = chunkId,
                Similarity = sim,
                Rank = 0
            });
        }

        var top = results
            .OrderByDescending(a => a.Similarity)
            .ThenBy(a => a.ChunkId, StringComparer.Ordinal)
            .Take(topK)
            .ToList();

        for (var i = 0; i < top.Count; i++)
            top[i] = new VectorSearchResult
            {
                ChunkId = top[i].ChunkId,
                Similarity = top[i].Similarity,
                Rank = i + 1
            };

        return top;
    }

    public void Clear() => _store.Clear();

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        var dot = 0f;
        var magA = 0f;
        var magB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA == 0 || magB == 0) return 0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}

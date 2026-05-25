// =============================================================================
// SemanticDoc/SemanticEmbeddingService.cs — embedding index (7A-2)
// =============================================================================
using Core.Retrieval.Embedding;
using Core.Retrieval.VectorStore;

namespace Core.Cognition.SemanticDoc;

public sealed class SemanticEmbeddingService
{
    private readonly IEmbeddingProvider _provider;
    private readonly InMemoryVectorStore _vectorStore;

    public SemanticEmbeddingService(IEmbeddingProvider provider, InMemoryVectorStore vectorStore)
    {
        _provider = provider;
        _vectorStore = vectorStore;
    }

    public string ModelName => _provider.ModelName;
    public int IndexedCount => _vectorStore.Count;

    public async Task IndexAsync(SemanticDocResult docResult)
    {
        var embeddings = new List<ChunkEmbedding>();

        foreach (var doc in docResult.Docs)
        {
            var text = doc.EnhancedText;
            if (string.IsNullOrEmpty(text)) continue;

            var vector = await _provider.GenerateEmbeddingAsync(text);
            if (vector is null || vector.Length == 0) continue;

            embeddings.Add(new ChunkEmbedding
            {
                ChunkId = doc.MethodId,
                Vector = vector,
                EmbeddingModel = _provider.ModelName,
                Dimensions = _provider.Dimensions,
                ContentHash = doc.ContentHash,
            });
        }

        _vectorStore.Index(embeddings);
    }

    public IReadOnlyList<VectorSearchResult> Search(string query, int topK = 20)
    {
        var queryVector = Task.Run(() => _provider.GenerateEmbeddingAsync(query)).Result;
        if (queryVector is null || queryVector.Length == 0)
            return Array.Empty<VectorSearchResult>();

        return _vectorStore.Search(queryVector, topK);
    }
}

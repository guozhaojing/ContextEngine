using Core.Retrieval.Chunking;

namespace Core.Retrieval.Embedding;

public sealed class EmbeddingBatcher
{
    private readonly int _maxTokensPerBatch;
    private readonly int _maxChunksPerBatch;

    public EmbeddingBatcher(int maxTokensPerBatch = 8192, int maxChunksPerBatch = 32)
    {
        _maxTokensPerBatch = maxTokensPerBatch;
        _maxChunksPerBatch = maxChunksPerBatch;
    }

    public IReadOnlyList<IReadOnlyList<CodeChunk>> CreateBatches(IReadOnlyList<CodeChunk> chunks)
    {
        var batches = new List<List<CodeChunk>>();
        var current = new List<CodeChunk>();
        var currentTokens = 0;

        foreach (var chunk in chunks)
        {
            var tokens = TokenEstimator.EstimateFromChunk(chunk.Content, chunk.Keywords.Count);

            if (current.Count >= _maxChunksPerBatch ||
                (currentTokens + tokens > _maxTokensPerBatch && current.Count > 0))
            {
                batches.Add(current);
                current = new List<CodeChunk>();
                currentTokens = 0;
            }

            current.Add(chunk);
            currentTokens += tokens;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }
}

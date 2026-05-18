namespace Core.Retrieval.Embedding;

public interface IEmbeddingProvider
{
    string ModelName { get; }
    int Dimensions { get; }
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}

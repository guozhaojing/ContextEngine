using System.Text.Json;
using Core.Retrieval.Chunking;

namespace Core.Retrieval.Embedding;

public sealed class EmbeddingExportService
{
    private readonly IEmbeddingProvider _provider;
    private readonly EmbeddingBatcher _batcher;
    private readonly EmbeddingCache _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EmbeddingExportService(
        IEmbeddingProvider provider,
        EmbeddingCache? cache = null,
        EmbeddingBatcher? batcher = null)
    {
        _provider = provider;
        _batcher = batcher ?? new EmbeddingBatcher();
        _cache = cache ?? new EmbeddingCache();
    }

    public async Task<IReadOnlyList<ChunkEmbedding>> GenerateAsync(
        IReadOnlyList<CodeChunk> chunks,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        await _cache.LoadAsync(ct);

        var embeddings = new List<ChunkEmbedding>(chunks.Count);
        var batches = _batcher.CreateBatches(chunks);
        var processed = 0;

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var chunk in batch)
            {
                var contentHash = EmbeddingCache.ComputeContentHash(chunk.Content);
                var cached = _cache.Get(contentHash);

                if (cached is not null)
                {
                    embeddings.Add(cached);
                    processed++;
                    continue;
                }

                var text = BuildEmbeddingText(chunk);
                var vector = await _provider.GenerateEmbeddingAsync(text, ct);
                var tokens = TokenEstimator.EstimateFromChunk(chunk.Content, chunk.Keywords.Count);

                var embedding = new ChunkEmbedding
                {
                    ChunkId = chunk.ChunkId,
                    EmbeddingModel = _provider.ModelName,
                    Dimensions = _provider.Dimensions,
                    Vector = vector,
                    TokenCount = tokens,
                    ContentHash = contentHash
                };

                _cache.Set(embedding);
                embeddings.Add(embedding);
                processed++;
            }

            progress?.Report(processed);
        }

        await _cache.SaveAsync(ct);

        return embeddings;
    }

    public async Task<string> SaveEmbeddingsAsync(
        IReadOnlyList<ChunkEmbedding> embeddings,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "chunk-embeddings.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            model = _provider.ModelName,
            dimensions = _provider.Dimensions,
            embeddingCount = embeddings.Count,
            embeddings = embeddings.Select(e => new
            {
                e.ChunkId,
                e.Dimensions,
                vector = e.Vector,
                e.ContentHash,
                e.TokenCount,
                e.CreatedAt
            })
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);

        return Path.GetFullPath(outputPath);
    }

    private static string BuildEmbeddingText(CodeChunk chunk)
    {
        // Rich text for embedding: title + summary + keywords + content
        var parts = new List<string?>
        {
            chunk.Title,
            chunk.Summary,
            string.Join(" ", chunk.Keywords),
            chunk.Content
        };

        return string.Join("\n", parts.Where(p => !string.IsNullOrEmpty(p)));
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.Retrieval.Embedding;

public sealed class EmbeddingCache
{
    private readonly string _cacheDir;
    private readonly Dictionary<string, ChunkEmbedding> _cache = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EmbeddingCache(string cacheDir = "")
    {
        _cacheDir = string.IsNullOrEmpty(cacheDir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "export", "embeddings")
            : cacheDir;
    }

    public static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..16];
    }

    public ChunkEmbedding? Get(string contentHash)
    {
        _cache.TryGetValue(contentHash, out var ce);
        return ce;
    }

    public void Set(ChunkEmbedding embedding)
    {
        _cache[embedding.ContentHash] = embedding;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        _cache.Clear();
        if (!Directory.Exists(_cacheDir)) return;

        var files = Directory.GetFiles(_cacheDir, "*.json");
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var embedding = JsonSerializer.Deserialize<ChunkEmbedding>(json, JsonOptions);
                if (embedding is not null)
                    _cache[embedding.ContentHash] = embedding;
            }
            catch { /* skip corrupt cache files */ }
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_cacheDir);

        foreach (var (hash, embedding) in _cache)
        {
            var path = Path.Combine(_cacheDir, $"{hash}.json");
            var json = JsonSerializer.Serialize(embedding, JsonOptions);
            await File.WriteAllTextAsync(path, json, ct);
        }
    }
}

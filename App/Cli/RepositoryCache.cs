// =============================================================================
// Cli/RepositoryCache.cs — persists graph state for fast reload
// =============================================================================
// Determinism: serialization is deterministic; same graph → identical cache file.
// Provenance: cache records the scan root and build timestamp.
// Replay: cache can be invalidated and regenerated.
// Grounding: cached state preserves all grounding metadata.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Graph;
using Core.Graph.Indexing;
using Core.Semantics;

namespace App.Cli;

public sealed class RepositoryCache
{
    private readonly string _cacheDir;

    public RepositoryCache(string cacheDir)
    {
        _cacheDir = cacheDir;
    }

    public bool HasCache(string repositoryPath)
    {
        var cachePath = GetCachePath(repositoryPath);
        return Directory.Exists(cachePath)
            && File.Exists(Path.Combine(cachePath, "cache.json"))
            && File.Exists(Path.Combine(cachePath, "graph.json"));
    }

    public async Task SaveAsync(string repositoryPath, CodeGraphBuildResult buildResult)
    {
        var cachePath = GetCachePath(repositoryPath);
        Directory.CreateDirectory(cachePath);

        var cacheInfo = new CacheInfo
        {
            RepositoryPath = repositoryPath,
            CachedAt = DateTime.UtcNow.ToString("O"),
            NodeCount = buildResult.Graph.Nodes.Count,
            EdgeCount = buildResult.Graph.Edges.Count,
            FactCount = buildResult.Graph.Facts.Count,
            SchemaVersion = buildResult.Graph.SchemaVersion,
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        var cacheJson = JsonSerializer.Serialize(cacheInfo, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(cachePath, "cache.json"), cacheJson);

        var graphJson = JsonSerializer.Serialize(buildResult.Graph, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(cachePath, "graph.json"), graphJson);
    }

    public async Task<CodeGraphBuildResult?> LoadAsync(string repositoryPath)
    {
        var cachePath = GetCachePath(repositoryPath);
        if (!HasCache(repositoryPath))
            return null;

        try
        {
            var cacheJson = await File.ReadAllTextAsync(Path.Combine(cachePath, "cache.json"));
            var cacheInfo = JsonSerializer.Deserialize<CacheInfo>(cacheJson);

            if (cacheInfo is null || cacheInfo.SchemaVersion != 1)
                return null;

            var graphJson = await File.ReadAllTextAsync(Path.Combine(cachePath, "graph.json"));
            var graph = JsonSerializer.Deserialize<CodeGraph>(graphJson);

            if (graph is null)
                return null;

            var index = GraphIndex.Build(graph);
            var symbolIndex = new SymbolReferenceIndex(graph);

            return new CodeGraphBuildResult
            {
                Graph = graph,
                Index = index,
                SymbolIndex = symbolIndex,
            };
        }
        catch
        {
            return null;
        }
    }

    public void Invalidate(string repositoryPath)
    {
        var cachePath = GetCachePath(repositoryPath);
        if (Directory.Exists(cachePath))
        {
            try { Directory.Delete(cachePath, recursive: true); }
            catch { }
        }
    }

    public CacheInfo? GetInfo(string repositoryPath)
    {
        var cachePath = GetCachePath(repositoryPath);
        var cacheFile = Path.Combine(cachePath, "cache.json");
        if (!File.Exists(cacheFile)) return null;

        try
        {
            var json = File.ReadAllText(cacheFile);
            return JsonSerializer.Deserialize<CacheInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    private string GetCachePath(string repositoryPath)
    {
        var normalized = repositoryPath
            .Replace('\\', '_')
            .Replace('/', '_')
            .Replace(':', '_');
        return Path.Combine(_cacheDir, normalized);
    }
}

public sealed class CacheInfo
{
    public string RepositoryPath { get; set; } = "";
    public string CachedAt { get; set; } = "";
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public int FactCount { get; set; }
    public int SchemaVersion { get; set; }
}

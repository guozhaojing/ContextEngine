// =============================================================================
// WebApi/WebApiSessionManager.cs — singleton session for web API (with cache)
// =============================================================================
using System.Text.Json;
using System.Text.Json.Serialization;
using App.Infrastructure;
using Core.Cognition;
using Core.Experience;
using Core.Graph;
using Core.Observability;

namespace App.WebApi;

public sealed class WebApiSessionManager
{
    private RepositorySession? _session;
    private InteractiveCognitionSession? _interactive;
    private readonly RepositoryLoader _loader;
    private readonly string _cacheDir;
    private string? _currentPath;
    private List<RepositoryHistoryEntry> _history = new();

    public WebApiSessionManager(string cacheDir)
    {
        _cacheDir = cacheDir;
        _loader = new RepositoryLoader(cacheDir);
        Narrator = new ArchitectureNarrator(MapGenerator);
        Complexity = new ComplexityAnalyzer(MapGenerator);
        LoadHistory();
    }

    private string HistoryFile => Path.Combine(_cacheDir, "repository-history.json");

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryFile))
            {
                var json = File.ReadAllText(HistoryFile);
                _history = JsonSerializer.Deserialize<List<RepositoryHistoryEntry>>(json) ?? new();
            }
        }
        catch { _history = new(); }
    }

    private void SaveHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryFile, json);
        }
        catch { }
    }

    private void AddOrUpdateHistory(string path, string name, int nodes, int edges)
    {
        var existing = _history.FirstOrDefault(h =>
            string.Equals(h.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Name = name;
            existing.NodeCount = nodes;
            existing.EdgeCount = edges;
            existing.LastUsed = DateTime.UtcNow.ToString("O");
        }
        else
        {
            _history.Add(new RepositoryHistoryEntry
            {
                Path = path,
                Name = name,
                NodeCount = nodes,
                EdgeCount = edges,
                LastUsed = DateTime.UtcNow.ToString("O"),
            });
        }
        SaveHistory();
    }

    public IReadOnlyList<RepositoryHistoryEntry> GetHistory() =>
        _history.OrderByDescending(h => h.LastUsed, StringComparer.Ordinal).ToList().AsReadOnly();

    public bool RemoveHistory(string path)
    {
        var removed = _history.RemoveAll(h =>
            string.Equals(h.Path, path, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            _loader.InvalidateCache(path);
            SaveHistory();
        }
        return removed > 0;
    }

    public void ClearAllHistory()
    {
        foreach (var h in _history)
            _loader.InvalidateCache(h.Path);
        _history.Clear();
        SaveHistory();
    }

    // ── 观测工具 ──
    public SystemMapGenerator MapGenerator { get; } = new();
    public ArchitectureNarrator Narrator { get; }
    public ComplexityAnalyzer Complexity { get; }

    public WebApiSessionManager() : this(GetDefaultCacheDir()) { }

    private static string GetDefaultCacheDir()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextEngine", "cache");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public RepositorySession? Session => _session;
    public InteractiveCognitionSession? Interactive => _interactive;
    public bool IsLoaded => _session?.IsLoaded ?? false;
    public string? CurrentPath => _currentPath;
    public bool IsFromCache { get; private set; }

    public async Task<LoadResult> LoadRepositoryAsync(string path, bool forceReload = false)
    {
        var result = await _loader.LoadAsync(path, forceReload);
        if (!result.Success)
            return new LoadResult { Success = false, Error = result.Error };

        _currentPath = path;
        IsFromCache = result.IsFromCache;

        return ActivateSession(path, result.BuildResult, fromCache: result.IsFromCache);
    }

    public async Task<LoadResult> ReloadAsync()
    {
        if (_currentPath is null)
            return new LoadResult { Success = false, Error = "请先加载仓库。" };

        _loader.InvalidateCache(_currentPath);
        return await LoadRepositoryAsync(_currentPath, forceReload: true);
    }

    public string ClearCache(string? path = null)
    {
        if (path is not null)
        {
            _loader.InvalidateCache(path);
            return $"已清除缓存: {path}";
        }
        else if (_currentPath is not null)
        {
            _loader.InvalidateCache(_currentPath);
            return $"已清除缓存: {_currentPath}";
        }
        return "未加载仓库，无法确定要清除的缓存。";
    }

    private LoadResult ActivateSession(string path, CodeGraphBuildResult buildResult, bool fromCache)
    {
        var sessionConfig = new RepositorySessionConfig
        {
            RepositoryPath = path,
            RepositoryName = Path.GetFileName(path.TrimEnd('/', '\\')),
        };

        _session = new RepositorySession(sessionConfig);
        _session.Load(buildResult);
        _interactive = new InteractiveCognitionSession(_session);

        AddOrUpdateHistory(path, _session.RepositoryName,
            buildResult.Graph.Nodes.Count, buildResult.Graph.Edges.Count);

        return new LoadResult
        {
            Success = true,
            RepositoryName = _session.RepositoryName,
            NodeCount = buildResult.Graph.Nodes.Count,
            EdgeCount = buildResult.Graph.Edges.Count,
            ProjectCount = buildResult.Graph.Nodes.Select(n => n.ProjectName).Distinct().Count(),
            FromCache = fromCache,
        };
    }

    public SessionInfo GetSessionInfo()
    {
        if (_session is null || !_session.IsLoaded)
            return new SessionInfo { IsLoaded = false };

        var snap = _session.Snapshot();
        return new SessionInfo
        {
            IsLoaded = true,
            RepositoryName = snap.RepositoryName,
            RepositoryPath = snap.RepositoryPath,
            NodeCount = snap.NodeCount,
            EdgeCount = snap.EdgeCount,
            TotalQueries = _interactive?.QueryCount ?? 0,
            History = _interactive?.History.Select(h => new HistoryItem
            {
                Question = h.Question,
                RoutedTo = h.RoutedTo,
                Confidence = h.Result.OverallConfidence.ToString(),
                EvidenceCount = h.Result.EvidenceCount,
            }).ToList() ?? new List<HistoryItem>(),
        };
    }

}

public sealed class LoadResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? RepositoryName { get; init; }
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public int ProjectCount { get; init; }
    public bool FromCache { get; init; }
}

public sealed class SessionInfo
{
    public bool IsLoaded { get; init; }
    public string RepositoryName { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public int TotalQueries { get; init; }
    public List<HistoryItem> History { get; init; } = new();
}

public sealed class HistoryItem
{
    public string Question { get; init; } = "";
    public string RoutedTo { get; init; } = "";
    public string Confidence { get; init; } = "";
    public int EvidenceCount { get; init; }
}

public sealed class RepositoryHistoryEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public string LastUsed { get; set; } = "";
}

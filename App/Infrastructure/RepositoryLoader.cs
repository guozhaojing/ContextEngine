using App.Cli;
using Core.Cognition.SemanticDoc;
using Core.Graph;
using Core.Graph.Analysis;
using Core.Graph.Analysis.Analyzers;
using Core.Graph.Analysis.GenericResolution;
using Core.Retrieval.Embedding;
using Core.Retrieval.VectorStore;
using Core.Scanning;

namespace App.Infrastructure;

public sealed class RepositoryLoader
{
    private readonly RepositoryCache _cache;

    public RepositoryLoader(string cacheDir)
    {
        _cache = new RepositoryCache(cacheDir);
    }

    public RepositoryCache Cache => _cache;

    public async Task<RepositoryLoadResult> LoadAsync(string path, bool forceReload = false,
        IProgress<LoadProgress>? progress = null)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
            return RepositoryLoadResult.Fail($"路径不存在: {path}");

        if (!forceReload)
        {
            var cached = await _cache.LoadAsync(path);
            if (cached is not null)
            {
                progress?.Report(new LoadProgress { Stage = "complete", IsComplete = true });
                return RepositoryLoadResult.FromCache(cached, path);
            }
        }

        progress?.Report(new LoadProgress { Stage = "parsing", CurrentFile = 0, TotalFiles = 0 });

        var scanner = new ProjectCodeScanner();
        var scan = await scanner.ScanAsync(path, progress: progress);

        progress?.Report(new LoadProgress { Stage = "building_graph" });
        var orchestrator = new CodeGraphAnalysisOrchestrator(DefaultAnalyzers());
        var buildResult = orchestrator.BuildAndAnalyze(scan);

        progress?.Report(new LoadProgress { Stage = "indexing_semantics" });
        var graphQuery = new GraphQueryService(buildResult.Index);
        var docBuilder = new SemanticDocBuilder(graphQuery);
        var docResult = docBuilder.BuildAll();

        var embeddingProvider = new FakeEmbeddingProvider();
        var vectorStore = new InMemoryVectorStore();
        var semanticSearch = new SemanticEmbeddingService(embeddingProvider, vectorStore);
        await semanticSearch.IndexAsync(docResult);

        buildResult = new CodeGraphBuildResult
        {
            Graph = buildResult.Graph,
            Index = buildResult.Index,
            SymbolIndex = buildResult.SymbolIndex,
            SemanticDocs = docResult,
            SemanticSearch = semanticSearch,
        };

        progress?.Report(new LoadProgress { Stage = "caching" });
        await _cache.SaveAsync(path, buildResult);

        progress?.Report(new LoadProgress { Stage = "complete", IsComplete = true });
        return RepositoryLoadResult.FromScan(buildResult, path);
    }

    public void InvalidateCache(string path) => _cache.Invalidate(path);

    public static IGraphAnalyzer[] DefaultAnalyzers() => new IGraphAnalyzer[]
    {
        new AspNetRouteAnalyzer(),
        new SpringBeanAnalyzer(),
        new SpringContextObjectAnalyzer(),
        new NHibernateAnalyzer(),
        new NhSessionGenericAnalyzer(),
    };
}

public sealed class RepositoryLoadResult
{
    public bool Success { get; private init; }
    public string? Error { get; private init; }
    public CodeGraphBuildResult BuildResult { get; private init; } = null!;
    public string RepositoryPath { get; private init; } = "";
    public string RepositoryName { get; private init; } = "";
    public int NodeCount { get; private init; }
    public int EdgeCount { get; private init; }
    public int FactCount { get; private init; }
    public int ProjectCount { get; private init; }
    public bool IsFromCache { get; private init; }

    public static RepositoryLoadResult FromCache(CodeGraphBuildResult buildResult, string path) => new()
    {
        Success = true,
        BuildResult = buildResult,
        RepositoryPath = path,
        RepositoryName = Path.GetFileName(path.TrimEnd('/', '\\')),
        NodeCount = buildResult.Graph.Nodes.Count,
        EdgeCount = buildResult.Graph.Edges.Count,
        FactCount = buildResult.Graph.Facts.Count,
        ProjectCount = buildResult.Graph.Nodes.Select(n => n.ProjectName).Distinct().Count(),
        IsFromCache = true,
    };

    public static RepositoryLoadResult FromScan(CodeGraphBuildResult buildResult, string path) => new()
    {
        Success = true,
        BuildResult = buildResult,
        RepositoryPath = path,
        RepositoryName = Path.GetFileName(path.TrimEnd('/', '\\')),
        NodeCount = buildResult.Graph.Nodes.Count,
        EdgeCount = buildResult.Graph.Edges.Count,
        FactCount = buildResult.Graph.Facts.Count,
        ProjectCount = buildResult.Graph.Nodes.Select(n => n.ProjectName).Distinct().Count(),
        IsFromCache = false,
    };

    public static RepositoryLoadResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
    };
}

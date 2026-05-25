// =============================================================================
// Experience/RepositorySession.cs — loaded repository cognition session
// =============================================================================
// Determinism: graph state is immutable; cognition results are replayable.
// Provenance: session tracks repository metadata and graph build provenance.
// Replay: session state can be snapshotted for regression comparison.
// Grounding: wires together all cognition engines against a loaded repository.
// =============================================================================

using Core.Cognition;
using Core.Cognition.SemanticDoc;
using Core.Graph;
using Core.Graph.Indexing;
using Core.Grounding.Confidence;
using Core.Semantics;

namespace Core.Experience;

public sealed class RepositorySession
{
    private readonly RepositorySessionConfig _config;

    public RepositorySession(RepositorySessionConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        SessionId = $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        CreatedAt = DateTime.UtcNow;
    }

    public string SessionId { get; }
    public DateTime CreatedAt { get; }

    public string RepositoryPath => _config.RepositoryPath;
    public string RepositoryName => _config.RepositoryName ?? Path.GetFileName(RepositoryPath.TrimEnd('/', '\\'));
    public bool IsLoaded { get; private set; }

    public CodeGraph? Graph { get; private set; }
    public GraphIndex? GraphIndex { get; private set; }
    public SymbolReferenceIndex? SymbolIndex { get; private set; }
    public GraphQueryService? QueryService { get; private set; }
    public SemanticEmbeddingService? SemanticSearch { get; private set; }

    public ConfidencePropagationEngine? ConfidenceEngine { get; private set; }

    public ArchitectureExplorer? ArchitectureExplorer { get; private set; }
    public ChangeImpactAnalyzer? ImpactAnalyzer { get; private set; }
    public BusinessCapabilityMapper? CapabilityMapper { get; private set; }
    public GroundedRootCauseExplorer? RootCauseExplorer { get; private set; }

    public QueryRouter? Router { get; private set; }
    public DeveloperQueryInterpreter? Interpreter { get; private set; }
    public CognitionResponseFormatter? Formatter { get; private set; }

    public SessionStats Stats { get; private set; } = new();

    public void Load(CodeGraphBuildResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(buildResult);

        Graph = buildResult.Graph;
        GraphIndex = buildResult.Index;
        SymbolIndex = buildResult.SymbolIndex ?? new SymbolReferenceIndex(buildResult.Graph);
        QueryService = new GraphQueryService(GraphIndex);
        SemanticSearch = buildResult.SemanticSearch;

        ConfidenceEngine = new ConfidencePropagationEngine(GraphIndex);

        ArchitectureExplorer = new ArchitectureExplorer(QueryService, SymbolIndex);
        ImpactAnalyzer = new ChangeImpactAnalyzer(QueryService, SymbolIndex, ConfidenceEngine);
        CapabilityMapper = new BusinessCapabilityMapper(QueryService, SymbolIndex);
        RootCauseExplorer = new GroundedRootCauseExplorer(QueryService, SymbolIndex);

        Router = new QueryRouter(this);
        Interpreter = new DeveloperQueryInterpreter();
        Formatter = new CognitionResponseFormatter();

        Stats.NodeCount = Graph.Nodes.Count;
        Stats.EdgeCount = Graph.Edges.Count;
        Stats.FactCount = Graph.Facts.Count;

        IsLoaded = true;

        _config.OnLoaded?.Invoke(this);
    }

    public CognitionResult Query(string userQuestion)
    {
        EnsureLoaded();

        var interpreted = Interpreter!.Interpret(userQuestion);
        var routing = Router!.Route(interpreted);
        var result = routing.Engine.Invoke(interpreted.NormalizedQuery);

        Stats.Increment(interpreted.Intent);

        if (_config.OnQueryCompleted is not null)
            _config.OnQueryCompleted.Invoke(this, routing.RoutedTo, result);

        return result;
    }

    public CognitionResult ExploreArchitecture(string query)
    {
        EnsureLoaded();
        return ArchitectureExplorer!.Explore(query);
    }

    public CognitionResult AnalyzeImpact(string query, string? targetMethod = null)
    {
        EnsureLoaded();
        return ImpactAnalyzer!.Analyze(query, targetMethod);
    }

    public CognitionResult MapCapabilities(string query)
    {
        EnsureLoaded();
        return CapabilityMapper!.Map(query);
    }

    public CognitionResult ExploreRootCause(string query)
    {
        EnsureLoaded();
        return RootCauseExplorer!.Explore(query);
    }

    public RepositorySnapshot Snapshot()
    {
        return new RepositorySnapshot
        {
            SessionId = SessionId,
            RepositoryName = RepositoryName,
            RepositoryPath = RepositoryPath,
            CreatedAt = CreatedAt.ToString("O"),
            NodeCount = Stats.NodeCount,
            EdgeCount = Stats.EdgeCount,
            FactCount = Stats.FactCount,
            TotalQueries = Stats.TotalQueries,
        };
    }

    private void EnsureLoaded()
    {
        if (!IsLoaded)
            throw new InvalidOperationException("Repository session is not loaded. Call Load() first.");
    }
}

public sealed class RepositorySessionConfig
{
    public required string RepositoryPath { get; init; }
    public string? RepositoryName { get; init; }
    public Action<RepositorySession>? OnLoaded { get; init; }
    public Action<RepositorySession, string, CognitionResult>? OnQueryCompleted { get; init; }

    public ConflictResolutionMode ConflictResolution { get; init; } = ConflictResolutionMode.SurfaceConflicts;

    public static RepositorySessionConfig Default => new()
    {
        RepositoryPath = Environment.CurrentDirectory,
        ConflictResolution = ConflictResolutionMode.SurfaceConflicts,
    };
}

public enum ConflictResolutionMode
{
    SurfaceConflicts = 0,
    BlockOnConflict = 1,
    WarnOnConflict = 2,
}

public sealed class SessionStats
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public int FactCount { get; set; }

    public int TotalQueries { get; set; }
    public int ArchitectureQueries { get; set; }
    public int ImpactQueries { get; set; }
    public int CapabilityQueries { get; set; }
    public int RootCauseQueries { get; set; }
    public int UnknownQueries { get; set; }

    public void Increment(DeveloperIntent intent)
    {
        TotalQueries++;
        switch (intent)
        {
            case DeveloperIntent.ExplainArchitecture: ArchitectureQueries++; break;
            case DeveloperIntent.AnalyzeImpact: ImpactQueries++; break;
            case DeveloperIntent.MapCapabilities: CapabilityQueries++; break;
            case DeveloperIntent.DebugIssue: RootCauseQueries++; break;
            default: UnknownQueries++; break;
        }
    }
}

public sealed class RepositorySnapshot : IEquatable<RepositorySnapshot>
{
    public string SessionId { get; init; } = "";
    public string RepositoryName { get; init; } = "";
    public string RepositoryPath { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public int FactCount { get; init; }
    public int TotalQueries { get; init; }

    public bool Equals(RepositorySnapshot? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(SessionId, other.SessionId)
            && NodeCount == other.NodeCount
            && EdgeCount == other.EdgeCount;
    }

    public override bool Equals(object? obj) => obj is RepositorySnapshot other && Equals(other);
    public override int GetHashCode() => SessionId.GetHashCode(StringComparison.Ordinal);
}

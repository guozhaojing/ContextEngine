// =============================================================================
// Experience/QueryRouter.cs — routes interpreted queries to cognition engines
// =============================================================================
// Determinism: routing is a pure function of interpreted intent and entities.
// Provenance: routing decisions are traceable and logged in session stats.
// Replay: QueryRouting implements IEquatable for regression comparison.
// Grounding: never routes to an engine that cannot produce grounded output.
// =============================================================================

using Core.Cognition;

namespace Core.Experience;

public sealed class QueryRouter
{
    private readonly RepositorySession _session;

    public QueryRouter(RepositorySession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public QueryRouting Route(InterpretedQuery interpreted)
    {
        var route = RouteByIntent(interpreted);

        return new QueryRouting
        {
            Interpreted = interpreted,
            RoutedTo = route.RoutedTo,
            Engine = route.Engine,
            Confidence = route.Confidence,
            Rationale = route.Rationale,
        };
    }

    private (string RoutedTo, Func<string, CognitionResult> Engine, RoutingConfidence Confidence, string Rationale)
        RouteByIntent(InterpretedQuery interpreted)
    {
        switch (interpreted.Intent)
        {
            case DeveloperIntent.ExplainArchitecture:
                return (
                    "ArchitectureExplorer",
                    q => _session.ExploreArchitecture(q),
                    interpreted.InterpretationConfidence >= InterpretationConfidence.Medium
                        ? RoutingConfidence.High : RoutingConfidence.Medium,
                    "Query contains architecture/structure keywords. Routing to Architecture Explorer."
                );

            case DeveloperIntent.AnalyzeImpact:
                return (
                    "ChangeImpactAnalyzer",
                    q => _session.AnalyzeImpact(q),
                    interpreted.Entities.Count > 0
                        ? RoutingConfidence.High : RoutingConfidence.Medium,
                    interpreted.Entities.Count > 0
                        ? $"Query requests impact analysis with target entities: {string.Join(", ", interpreted.Entities)}."
                        : "Query requests impact analysis. No specific targets detected."
                );

            case DeveloperIntent.DebugIssue:
                return (
                    "GroundedRootCauseExplorer",
                    q => _session.ExploreRootCause(q),
                    interpreted.Entities.Count > 0
                        ? RoutingConfidence.High : RoutingConfidence.Medium,
                    interpreted.Entities.Count > 0
                        ? $"Debug query with target entities: {string.Join(", ", interpreted.Entities)}."
                        : "Debug query detected. Routing to root cause explorer."
                );

            case DeveloperIntent.MapCapabilities:
                return (
                    "BusinessCapabilityMapper",
                    q => _session.MapCapabilities(q),
                    interpreted.InterpretationConfidence >= InterpretationConfidence.Medium
                        ? RoutingConfidence.High : RoutingConfidence.Medium,
                    "Query asks 'where is' or 'how does' — routing to capability mapper."
                );

            default:
                return (
                    "ArchitectureExplorer",
                    q => _session.ExploreArchitecture(q),
                    RoutingConfidence.Low,
                    "Unclear intent. Defaulting to architecture exploration."
                );
        }
    }
}

public sealed class QueryRouting : IEquatable<QueryRouting>
{
    public required InterpretedQuery Interpreted { get; init; }
    public required string RoutedTo { get; init; }
    public required Func<string, CognitionResult> Engine { get; init; }
    public RoutingConfidence Confidence { get; init; }
    public string Rationale { get; init; } = "";

    public CognitionResult Execute(string query = "")
    {
        return Engine(string.IsNullOrEmpty(query) ? Interpreted.NormalizedQuery : query);
    }

    public bool Equals(QueryRouting? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(RoutedTo, other.RoutedTo)
            && Confidence == other.Confidence
            && Interpreted.Equals(other.Interpreted);
    }

    public override bool Equals(object? obj) => obj is QueryRouting other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        RoutedTo.GetHashCode(StringComparison.Ordinal),
        Confidence);
}

public enum RoutingConfidence
{
    Low = 0,
    Medium = 1,
    High = 2,
}

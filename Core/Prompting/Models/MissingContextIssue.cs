// =============================================================================
// Models/MissingContextIssue.cs — detected gap in context completeness
// =============================================================================

namespace Core.Prompting.Models;

public sealed class MissingContextIssue
{
    public required string IssueId { get; init; }
    public required MissingContextKind Kind { get; init; }
    public required string Description { get; init; }
    public string? AffectedEntity { get; init; }
    public string? AffectedRoute { get; init; }
    public string? AffectedMethod { get; init; }
    public double Severity { get; init; }
    public string Recommendation { get; init; } = "";
}

public enum MissingContextKind
{
    MissingRepositoryImplementation,
    UnresolvedEntityMapping,
    LowConfidencePath,
    DisconnectedSegment,
    DynamicBlindSpot,
    MissingRouteCoverage,
    IncompleteCallChain,
    UnknownDependency
}

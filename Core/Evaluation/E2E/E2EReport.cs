// =============================================================================
// E2E/E2EReport.cs — per-case and aggregate E2E results
// =============================================================================

namespace Core.Evaluation.E2E;

public sealed class E2EReport
{
    public required string ReportId { get; init; }
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public required IReadOnlyList<E2ECaseResult> CaseResults { get; init; }
    public E2EAggregate? Aggregate { get; init; }
    public int TotalCases => CaseResults.Count;
    public int PassedCases => CaseResults.Count(r => r.Passed);
    public int FailedCases => CaseResults.Count(r => !r.Passed);
    public double PassRate => TotalCases > 0 ? (double)PassedCases / TotalCases : 0;
}

public sealed class E2ECaseResult
{
    public required E2ECase Case { get; init; }
    public bool Passed { get; init; }
    public required E2EPipelineMetrics Metrics { get; init; }
    public IReadOnlyList<E2EFailure> Failures { get; init; } = Array.Empty<E2EFailure>();
    public long TotalElapsedMs { get; init; }
    public string Summary { get; init; } = "";
}

public sealed class E2EPipelineMetrics
{
    public string? DetectedIntent { get; init; }
    public bool IntentCorrect { get; init; }
    public int RetrievalCandidateCount { get; init; }
    public int ContextPathCount { get; init; }
    public int ContextEntityCount { get; init; }
    public int ContextTableCount { get; init; }
    public int ContextRuleCount { get; init; }
    public int ContextMethodCount { get; init; }
    public int PromptSectionCount { get; init; }
    public int PromptTokenEstimate { get; init; }
    public string StrategyUsed { get; init; } = "";
    public Prompt.PromptQualityScores? QualityScores { get; init; }
    public double EntityCoverage { get; init; }
    public double TableCoverage { get; init; }
    public double RouteCoverage { get; init; }
    public bool HasMissingContextIssues { get; init; }
    public int MissingIssueCount { get; init; }
}

public sealed class E2EFailure
{
    public required string Category { get; init; }
    public required string Description { get; init; }
    public double Severity { get; init; } = 0.5;
}

public sealed class E2EAggregate
{
    public double AvgCoherence { get; init; }
    public double AvgCompleteness { get; init; }
    public double AvgStructure { get; init; }
    public double AvgActionability { get; init; }
    public double AvgEntityCoverage { get; init; }
    public double AvgTableCoverage { get; init; }
    public double AvgRouteCoverage { get; init; }
    public double IntentAccuracy { get; init; }
    public double AvgPromptTokens { get; init; }
    public long AvgElapsedMs { get; init; }
    public double AvgSectionCount { get; init; }
}

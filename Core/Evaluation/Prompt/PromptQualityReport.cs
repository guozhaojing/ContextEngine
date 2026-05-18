// =============================================================================
// Prompt/PromptQualityReport.cs — aggregated prompt quality report
// =============================================================================

namespace Core.Evaluation.Prompt;

public sealed class PromptQualityReport
{
    public required string ReportId { get; init; }
    public required string PromptId { get; init; }
    public required string Query { get; init; }
    public required PromptQualityScores Scores { get; init; }
    public required IReadOnlyList<QualityDetail> Details { get; init; }
    public string GeneratedAt { get; init; } = "";
    public int WarningCount => Details.Count(d => d.Severity == "warning");
    public int ErrorCount => Details.Count(d => d.Severity == "error");
    public int InfoCount => Details.Count(d => d.Severity == "info");
}

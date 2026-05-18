// =============================================================================
// Systems/SystemConsistencyReport.cs — combined system consistency report
// =============================================================================

namespace Core.Evaluation.Systems;

public sealed class SystemConsistencyReport
{
    public required string ReportId { get; init; }
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public required StrategyCoverageReport StrategyCoverage { get; init; }
    public required IReadOnlyList<DriftDetectionResult> DriftResults { get; init; }
    public int CaseCount { get; init; }
    public int DriftDetectedCount { get; init; }
    public int TotalDriftIssues { get; init; }
    public bool IsConsistent => DriftDetectedCount == 0 && StrategyCoverage.IsFullyCovered;
    public double DriftRate => CaseCount > 0 ? (double)DriftDetectedCount / CaseCount : 0;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            parts.Add($"Strategy coverage: {(StrategyCoverage.IsFullyCovered ? "FULL" : "INCOMPLETE")} ({StrategyCoverage.CoverageRatio:P0})");
            parts.Add($"Drift detected in {DriftDetectedCount}/{CaseCount} cases ({DriftRate:P0})");
            parts.Add($"Total drift issues: {TotalDriftIssues}");
            parts.Add(IsConsistent ? "System is consistent." : "System has consistency issues.");
            return string.Join(" | ", parts);
        }
    }
}

// =============================================================================
// Export/EvaluationExportService.cs — exports evaluation results to JSON/MD
// =============================================================================

using System.Text.Json;
using Core.Evaluation.E2E;
using Core.Evaluation.Prompt;
using Core.Evaluation.Systems;

namespace Core.Evaluation.Export;

public sealed class EvaluationExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ═══════════════════════════════════════════════════════════════
    // E2E Benchmark
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> SaveE2EBenchmarkAsync(
        E2EReport report,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "e2e-benchmark.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = report.GeneratedAt,
            report.ReportId,
            report.TotalCases,
            report.PassedCases,
            report.FailedCases,
            report.PassRate,
            aggregate = report.Aggregate is not null ? new
            {
                report.Aggregate.AvgCoherence,
                report.Aggregate.AvgCompleteness,
                report.Aggregate.AvgStructure,
                report.Aggregate.AvgActionability,
                report.Aggregate.AvgEntityCoverage,
                report.Aggregate.AvgTableCoverage,
                report.Aggregate.AvgRouteCoverage,
                report.Aggregate.IntentAccuracy,
                report.Aggregate.AvgPromptTokens,
                report.Aggregate.AvgElapsedMs,
                report.Aggregate.AvgSectionCount
            } : null,
            cases = report.CaseResults.Select(r => new
            {
                caseId = r.Case.CaseId,
                query = r.Case.Query,
                passed = r.Passed,
                metrics = new
                {
                    detectedIntent = r.Metrics.DetectedIntent,
                    intentCorrect = r.Metrics.IntentCorrect,
                    retrievalCandidates = r.Metrics.RetrievalCandidateCount,
                    contextPaths = r.Metrics.ContextPathCount,
                    contextEntities = r.Metrics.ContextEntityCount,
                    contextTables = r.Metrics.ContextTableCount,
                    contextRules = r.Metrics.ContextRuleCount,
                    contextMethods = r.Metrics.ContextMethodCount,
                    promptSections = r.Metrics.PromptSectionCount,
                    promptTokens = r.Metrics.PromptTokenEstimate,
                    strategyUsed = r.Metrics.StrategyUsed,
                    qualityScores = r.Metrics.QualityScores is not null ? new
                    {
                        r.Metrics.QualityScores.Coherence,
                        r.Metrics.QualityScores.Completeness,
                        r.Metrics.QualityScores.Structural,
                        r.Metrics.QualityScores.Actionability,
                        r.Metrics.QualityScores.Overall
                    } : null,
                    entityCoverage = r.Metrics.EntityCoverage,
                    tableCoverage = r.Metrics.TableCoverage,
                    routeCoverage = r.Metrics.RouteCoverage,
                    hasMissingIssues = r.Metrics.HasMissingContextIssues,
                    missingIssueCount = r.Metrics.MissingIssueCount
                },
                failures = r.Failures.Select(f => new
                {
                    f.Category,
                    f.Description,
                    f.Severity
                }),
                totalElapsedMs = r.TotalElapsedMs,
                summary = r.Summary
            })
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);
        return Path.GetFullPath(outputPath);
    }

    // ═══════════════════════════════════════════════════════════════
    // Prompt Quality Report
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> SavePromptQualityReportAsync(
        PromptQualityReport report,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "prompt-quality-report.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = report.GeneratedAt,
            report.ReportId,
            report.PromptId,
            report.Query,
            scores = new
            {
                report.Scores.Coherence,
                report.Scores.Completeness,
                report.Scores.Structural,
                report.Scores.Actionability,
                report.Scores.Overall
            },
            report.WarningCount,
            report.ErrorCount,
            report.InfoCount,
            details = report.Details.Select(d => new
            {
                d.Dimension,
                d.Finding,
                d.Severity
            })
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);
        return Path.GetFullPath(outputPath);
    }

    // ═══════════════════════════════════════════════════════════════
    // System Consistency Report
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> SaveSystemConsistencyReportAsync(
        SystemConsistencyReport report,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "system-consistency-report.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = report.GeneratedAt,
            report.ReportId,
            report.IsConsistent,
            report.Summary,
            strategyCoverage = new
            {
                report.StrategyCoverage.ReportId,
                report.StrategyCoverage.TotalIntents,
                report.StrategyCoverage.CoveredIntents,
                report.StrategyCoverage.MissingIntents,
                report.StrategyCoverage.CoverageRatio,
                report.StrategyCoverage.IsFullyCovered,
                entries = report.StrategyCoverage.Entries.Select(e => new
                {
                    e.Intent,
                    e.HasStrategy,
                    e.StrategyName,
                    e.Status
                })
            },
            drift = new
            {
                report.CaseCount,
                report.DriftDetectedCount,
                report.TotalDriftIssues,
                report.DriftRate,
                results = report.DriftResults.Select(d => new
                {
                    d.ContextQuery,
                    d.DriftDetected,
                    d.TotalIssues,
                    issues = d.Issues.Select(i => new
                    {
                        i.IssueType,
                        i.Description,
                        i.Severity
                    })
                })
            }
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);
        return Path.GetFullPath(outputPath);
    }

    // ═══════════════════════════════════════════════════════════════
    // Convenience: Save All
    // ═══════════════════════════════════════════════════════════════

    public async Task SaveAllAsync(
        E2EReport e2eReport,
        PromptQualityReport qualityReport,
        SystemConsistencyReport consistencyReport,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        await SaveE2EBenchmarkAsync(e2eReport, outputDirectory, ct);
        await SavePromptQualityReportAsync(qualityReport, outputDirectory, ct);
        await SaveSystemConsistencyReportAsync(consistencyReport, outputDirectory, ct);
    }
}

// =============================================================================
// E2E/E2ERunner.cs — full pipeline end-to-end benchmark runner
// =============================================================================
// 【设计】模拟完整 Query → Intent → Retrieval → Context → Prompt 链路
// 【边界】只读消费现有模块，不修改任何 pipeline 代码
// =============================================================================

using System.Diagnostics;
using Core.Context.Models;
using Core.Evaluation.Prompt;
using Core.Evaluation.Systems;
using Core.Graph;
using Core.Prompting;
using Core.Prompting.Models;
using Core.Prompting.QueryExecution;
using Core.QueryUnderstanding;
using Core.Retrieval.Retrieval;
using ContextAssemblerAlias = Core.Context.Assembly.ContextAssembler;

namespace Core.Evaluation.E2E;

public sealed class E2ERunner
{
    private readonly ContextAssemblerAlias _contextAssembler;
    private readonly PromptAssembler _promptAssembler;
    private readonly PromptOrchestrator _orchestrator;
    private readonly StrategyCoverageValidator _strategyValidator;
    private readonly ContextDriftDetector _driftDetector;
    private readonly PromptQualityScorer _qualityScorer;

    public E2ERunner(GraphQueryService queryService)
    {
        _contextAssembler = new ContextAssemblerAlias(queryService);
        _promptAssembler = new PromptAssembler();
        _orchestrator = new PromptOrchestrator();
        _strategyValidator = new StrategyCoverageValidator();
        _driftDetector = new ContextDriftDetector();
        _qualityScorer = new PromptQualityScorer();
    }

    public E2EReport Run(IReadOnlyList<E2ECase> cases, RetrievalResult retrievalResult)
    {
        var caseResults = new List<E2ECaseResult>();
        var sw = new Stopwatch();

        foreach (var testCase in cases)
        {
            sw.Restart();

            var result = RunSingle(testCase, retrievalResult);

            sw.Stop();

            var finalResult = new E2ECaseResult
            {
                Case = result.Case,
                Passed = result.Passed,
                Metrics = result.Metrics,
                Failures = result.Failures,
                TotalElapsedMs = sw.ElapsedMilliseconds,
                Summary = result.Summary
            };

            caseResults.Add(finalResult);
        }

        var aggregate = ComputeAggregate(caseResults);

        return new E2EReport
        {
            ReportId = $"e2e-{DateTime.Now:yyyyMMddHHmmss}",
            CaseResults = caseResults,
            Aggregate = aggregate
        };
    }

    public E2ECaseResult RunSingle(E2ECase testCase, RetrievalResult retrievalResult)
    {
        var failures = new List<E2EFailure>();

        var intent = QueryIntentClassifier.Classify(testCase.Query);
        var intentCorrect = testCase.ExpectedIntent is null ||
                            intent.ToString().Equals(testCase.ExpectedIntent, StringComparison.OrdinalIgnoreCase);

        if (!intentCorrect && testCase.ExpectedIntent is not null)
        {
            failures.Add(new E2EFailure
            {
                Category = "Intent",
                Description = $"Expected intent '{testCase.ExpectedIntent}' but got '{intent}'.",
                Severity = 0.7
            });
        }

        var structuredContext = _contextAssembler.Assemble(retrievalResult);

        var entityCoverage = ComputeCoverage(structuredContext.Entities, testCase.ExpectedEntities);
        var tableCoverage = ComputeCoverage(structuredContext.Tables, testCase.ExpectedTables);
        var routeCoverage = ComputeCoverage(structuredContext.Routes, testCase.ExpectedRoutes);

        if (testCase.ExpectedEntities.Count > 0 && entityCoverage < 0.5)
        {
            failures.Add(new E2EFailure
            {
                Category = "EntityCoverage",
                Description = $"Entity coverage {entityCoverage:P0} below threshold. Expected: {string.Join(", ", testCase.ExpectedEntities)}, Got: {string.Join(", ", structuredContext.Entities.Take(10))}",
                Severity = 0.6
            });
        }

        if (testCase.ExpectedTables.Count > 0 && tableCoverage < 0.5)
        {
            failures.Add(new E2EFailure
            {
                Category = "TableCoverage",
                Description = $"Table coverage {tableCoverage:P0} below threshold.",
                Severity = 0.5
            });
        }

        var promptContext = _promptAssembler.Assemble(structuredContext, retrievalResult);

        var orchestrationResult = _orchestrator.Execute(structuredContext);

        var qualityScores = _qualityScorer.Score(orchestrationResult.FinalPrompt);

        if (qualityScores.Coherence < testCase.MinCoherenceScore)
        {
            failures.Add(new E2EFailure
            {
                Category = "Coherence",
                Description = $"Coherence {qualityScores.Coherence:F2} below threshold {testCase.MinCoherenceScore}.",
                Severity = 0.5
            });
        }

        if (qualityScores.Completeness < testCase.MinCompletenessScore)
        {
            failures.Add(new E2EFailure
            {
                Category = "Completeness",
                Description = $"Completeness {qualityScores.Completeness:F2} below threshold {testCase.MinCompletenessScore}.",
                Severity = 0.5
            });
        }

        if (qualityScores.Structural < testCase.MinStructuralScore)
        {
            failures.Add(new E2EFailure
            {
                Category = "Structural",
                Description = $"Structural score {qualityScores.Structural:F2} below threshold {testCase.MinStructuralScore}.",
                Severity = 0.4
            });
        }

        if (testCase.MinSemanticPaths > 0 && structuredContext.SemanticPaths.Count < testCase.MinSemanticPaths)
        {
            failures.Add(new E2EFailure
            {
                Category = "PathCoverage",
                Description = $"Expected at least {testCase.MinSemanticPaths} semantic paths, got {structuredContext.SemanticPaths.Count}.",
                Severity = 0.6
            });
        }

        if (testCase.MinBusinessRules > 0 && structuredContext.BusinessRules.Count < testCase.MinBusinessRules)
        {
            failures.Add(new E2EFailure
            {
                Category = "RuleCoverage",
                Description = $"Expected at least {testCase.MinBusinessRules} business rules, got {structuredContext.BusinessRules.Count}.",
                Severity = 0.5
            });
        }

        var driftResult = _driftDetector.Detect(structuredContext, promptContext, retrievalResult);

        if (driftResult.DriftDetected)
        {
            foreach (var issue in driftResult.Issues)
            {
                failures.Add(new E2EFailure
                {
                    Category = $"Drift:{issue.IssueType}",
                    Description = issue.Description,
                    Severity = issue.Severity
                });
            }
        }

        var missingIssues = promptContext.MissingContextIssues;

        var passed = failures.Count == 0;

        return new E2ECaseResult
        {
            Case = testCase,
            Passed = passed,
            Metrics = new E2EPipelineMetrics
            {
                DetectedIntent = intent.ToString(),
                IntentCorrect = intentCorrect,
                RetrievalCandidateCount = retrievalResult.Candidates.Count,
                ContextPathCount = structuredContext.SemanticPaths.Count,
                ContextEntityCount = structuredContext.Entities.Count,
                ContextTableCount = structuredContext.Tables.Count,
                ContextRuleCount = structuredContext.BusinessRules.Count,
                ContextMethodCount = structuredContext.CompressedMethods.Count,
                PromptSectionCount = orchestrationResult.FinalPrompt.Sections.Count,
                PromptTokenEstimate = orchestrationResult.FinalPrompt.TokenEstimate,
                StrategyUsed = orchestrationResult.Strategy,
                QualityScores = qualityScores,
                EntityCoverage = entityCoverage,
                TableCoverage = tableCoverage,
                RouteCoverage = routeCoverage,
                HasMissingContextIssues = missingIssues.Count > 0,
                MissingIssueCount = missingIssues.Count
            },
            Failures = failures,
            Summary = passed
                ? "All checks passed."
                : $"{failures.Count} failure(s): {string.Join("; ", failures.Take(3).Select(f => f.Category))}"
        };
    }

    public SystemConsistencyReport RunConsistencyCheck(
        IReadOnlyList<E2ECase> cases,
        RetrievalResult retrievalResult)
    {
        var strategyReport = _strategyValidator.Validate();
        var driftResults = new List<DriftDetectionResult>();

        foreach (var testCase in cases)
        {
            var structuredContext = _contextAssembler.Assemble(retrievalResult);
            var promptContext = _promptAssembler.Assemble(structuredContext, retrievalResult);
            var drift = _driftDetector.Detect(structuredContext, promptContext, retrievalResult);
            driftResults.Add(drift);
        }

        return new SystemConsistencyReport
        {
            ReportId = $"consistency-{DateTime.Now:yyyyMMddHHmmss}",
            StrategyCoverage = strategyReport,
            DriftResults = driftResults,
            CaseCount = cases.Count,
            DriftDetectedCount = driftResults.Count(d => d.DriftDetected),
            TotalDriftIssues = driftResults.Sum(d => d.Issues.Count)
        };
    }

    private static double ComputeCoverage(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (expected.Count == 0) return 1.0;
        var matchCount = expected.Count(e =>
            actual.Any(a => a.Contains(e, StringComparison.OrdinalIgnoreCase)));
        return (double)matchCount / expected.Count;
    }

    private static E2EAggregate ComputeAggregate(IReadOnlyList<E2ECaseResult> results)
    {
        if (results.Count == 0) return new E2EAggregate();

        return new E2EAggregate
        {
            AvgCoherence = Math.Round(results.Average(r => r.Metrics.QualityScores?.Coherence ?? 0), 3),
            AvgCompleteness = Math.Round(results.Average(r => r.Metrics.QualityScores?.Completeness ?? 0), 3),
            AvgStructure = Math.Round(results.Average(r => r.Metrics.QualityScores?.Structural ?? 0), 3),
            AvgActionability = Math.Round(results.Average(r => r.Metrics.QualityScores?.Actionability ?? 0), 3),
            AvgEntityCoverage = Math.Round(results.Average(r => r.Metrics.EntityCoverage), 3),
            AvgTableCoverage = Math.Round(results.Average(r => r.Metrics.TableCoverage), 3),
            AvgRouteCoverage = Math.Round(results.Average(r => r.Metrics.RouteCoverage), 3),
            IntentAccuracy = Math.Round((double)results.Count(r => r.Metrics.IntentCorrect) / results.Count, 3),
            AvgPromptTokens = (long)results.Average(r => r.Metrics.PromptTokenEstimate),
            AvgElapsedMs = (long)results.Average(r => r.TotalElapsedMs),
            AvgSectionCount = Math.Round(results.Average(r => r.Metrics.PromptSectionCount), 1)
        };
    }
}

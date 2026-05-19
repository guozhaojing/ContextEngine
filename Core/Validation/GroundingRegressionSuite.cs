// =============================================================================
// Validation/GroundingRegressionSuite.cs — grounding regression test suite
// =============================================================================
// Runs all grounding regression tests and reports failures.
// Each test verifies that a specific grounding requirement is met.
// =============================================================================

using Core.Context;
using Core.Retrieval.Retrieval;
using Core.Truth;

namespace Core.Validation;

public sealed class GroundingRegressionSuite
{
    private readonly List<SemanticRegressionCase> _cases = new();

    public IReadOnlyList<SemanticRegressionCase> Cases => _cases.AsReadOnly();

    public void Register(SemanticRegressionCase testCase)
    {
        _cases.Add(testCase);
    }

    public void RegisterDefaults()
    {
        Register(SemanticRegressionCase.CreateHallucinationTest("API endpoint flow"));
        Register(SemanticRegressionCase.CreateHallucinationTest("database schema"));
        Register(SemanticRegressionCase.CreateHallucinationTest("entity relationship"));
        Register(SemanticRegressionCase.CreateHallucinationTest("business rule validation"));

        Register(SemanticRegressionCase.CreateGroundingTest("API to database", 3));
        Register(SemanticRegressionCase.CreateGroundingTest("data access layer", 2));

        Register(SemanticRegressionCase.CreateDeterminismTest("API endpoint flow"));
        Register(SemanticRegressionCase.CreateDeterminismTest("entity mapping"));
    }

    public GroundingRegressionReport Run(
        Func<RetrievalQuery, RetrievalResult> retrieve,
        Func<RetrievalResult, IReadOnlyList<ContextSection>> assemble)
    {
        var results = new List<RegressionTestResult>();

        foreach (var testCase in _cases)
        {
            var result = RunSingle(testCase, retrieve, assemble);
            results.Add(result);
        }

        return new GroundingRegressionReport
        {
            TotalTests = results.Count,
            Passed = results.Count(r => r.Passed),
            Failed = results.Count(r => !r.Passed),
            Results = results,
            RunAt = DateTime.UtcNow.ToString("O"),
        };
    }

    private RegressionTestResult RunSingle(
        SemanticRegressionCase testCase,
        Func<RetrievalQuery, RetrievalResult> retrieve,
        Func<RetrievalResult, IReadOnlyList<ContextSection>> assemble)
    {
        var failures = new List<string>();

        try
        {
            var retrievalResult = retrieve(testCase.Query);
            var sections = assemble(retrievalResult);

            CheckForbiddenTitles(testCase, sections, failures);
            CheckGrounding(testCase, sections, failures);
            CheckRequiredChunkIds(testCase, retrievalResult, failures);
            CheckForbiddenPatterns(testCase, sections, failures);
            CheckUngroundedCount(testCase, sections, failures);
            CheckSymbolBinding(testCase, sections, failures);
            CheckSourceFile(testCase, sections, failures);
        }
        catch (Exception ex)
        {
            failures.Add($"Exception: {ex.Message}");
        }

        return new RegressionTestResult
        {
            CaseId = testCase.CaseId,
            Name = testCase.Name,
            Category = testCase.Category,
            Passed = failures.Count == 0,
            Failures = failures,
        };
    }

    private static void CheckForbiddenTitles(
        SemanticRegressionCase testCase,
        IReadOnlyList<ContextSection> sections,
        List<string> failures)
    {
        foreach (var section in sections)
        {
            foreach (var forbidden in testCase.ForbiddenSectionTitles)
            {
                if (section.Title.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"Forbidden title pattern '{forbidden}' found in section '{section.Title}'.");
                }
            }
        }
    }

    private static void CheckGrounding(
        SemanticRegressionCase testCase,
        IReadOnlyList<ContextSection> sections,
        List<string> failures)
    {
        foreach (var section in sections)
        {
            if (section.Evidence is not null && section.Evidence.EvidenceStrength < EvidenceStrength.SyntaxPattern)
            {
                failures.Add($"Section '{section.Title}' has insufficient evidence: {section.Evidence?.EvidenceStrength}.");
            }
        }
    }

    private static void CheckRequiredChunkIds(
        SemanticRegressionCase testCase,
        RetrievalResult result,
        List<string> failures)
    {
        foreach (var requiredId in testCase.RequiredChunkIds)
        {
            if (!result.Candidates.Any(c =>
                StringComparer.Ordinal.Equals(c.Chunk.ChunkId, requiredId)))
            {
                failures.Add($"Required chunk '{requiredId}' not found in results.");
            }
        }
    }

    private static void CheckForbiddenPatterns(
        SemanticRegressionCase testCase,
        IReadOnlyList<ContextSection> sections,
        List<string> failures)
    {
        foreach (var section in sections)
        {
            foreach (var pattern in testCase.ForbiddenPatterns)
            {
                if (section.Content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"Forbidden pattern '{pattern}' in section '{section.Title}'.");
                }
            }
        }
    }

    private static void CheckUngroundedCount(
        SemanticRegressionCase testCase,
        IReadOnlyList<ContextSection> sections,
        List<string> failures)
    {
        var ungrounded = sections.Count(s => s.IsGrounded == false);
        if (ungrounded > testCase.MaxUngroundedSections)
        {
            failures.Add($"Expected max {testCase.MaxUngroundedSections} ungrounded sections, got {ungrounded}.");
        }
    }

    private static void CheckSymbolBinding(
        SemanticRegressionCase testCase,
        IReadOnlyList<ContextSection> sections,
        List<string> failures)
    {
        if (!testCase.RequireSymbolBinding) return;

        foreach (var section in sections)
        {
            if (section.SourceSymbolHandles.Count == 0 && section.SourceNodeIds.Count == 0)
            {
                failures.Add($"Section '{section.Title}' has no symbol binding or node references.");
            }
        }
    }

    private static void CheckSourceFile(
        SemanticRegressionCase testCase,
        IReadOnlyList<ContextSection> sections,
        List<string> failures)
    {
        if (!testCase.RequireSourceFile) return;

        foreach (var section in sections)
        {
            if (section.Evidence is not null
                && section.Evidence.SourceFiles.Count == 0
                && section.SourceNodeIds.Count > 0)
            {
                failures.Add($"Section '{section.Title}' references nodes but has no source files.");
            }
        }
    }
}

public sealed class GroundingRegressionReport
{
    public int TotalTests { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public required IReadOnlyList<RegressionTestResult> Results { get; init; }
    public string RunAt { get; init; } = "";

    public bool AllPassed => Failed == 0;
}

public sealed class RegressionTestResult
{
    public required string CaseId { get; init; }
    public required string Name { get; init; }
    public RegressionCategory Category { get; init; }
    public bool Passed { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }
}

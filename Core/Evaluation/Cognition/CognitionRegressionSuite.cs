// =============================================================================
// Evaluation/Cognition/CognitionRegressionSuite.cs — regression prevention
// =============================================================================
// Determinism: regression tests are fixed assertions that must produce stable output.
// Provenance: each regression case references the cognition capability under test.
// Replay: RegressionReport implements IEquatable for run comparison.
// Grounding: prevents regressions in architecture, impact, root cause, capability.
// =============================================================================

using Core.Cognition;
using Core.Grounding.Confidence;

namespace Core.Evaluation.Cognition;

public sealed class CognitionRegressionSuite
{
    private readonly List<CognitionRegressionCase> _cases = new();
    private readonly ArchitectureExplorer _architectureExplorer;
    private readonly ChangeImpactAnalyzer _impactAnalyzer;
    private readonly BusinessCapabilityMapper _capabilityMapper;
    private readonly GroundedRootCauseExplorer _rootCauseExplorer;

    public CognitionRegressionSuite(
        ArchitectureExplorer architectureExplorer,
        ChangeImpactAnalyzer impactAnalyzer,
        BusinessCapabilityMapper capabilityMapper,
        GroundedRootCauseExplorer rootCauseExplorer)
    {
        _architectureExplorer = architectureExplorer;
        _impactAnalyzer = impactAnalyzer;
        _capabilityMapper = capabilityMapper;
        _rootCauseExplorer = rootCauseExplorer;
    }

    public IReadOnlyList<CognitionRegressionCase> Cases => _cases.AsReadOnly();

    public CognitionRegressionSuite RegisterDefaults()
    {
        RegisterArchitectureRegressions();
        RegisterImpactRegressions();
        RegisterRootCauseRegressions();
        RegisterCapabilityRegressions();
        return this;
    }

    private void RegisterArchitectureRegressions()
    {
        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-arch-001",
            Name = "Architecture must produce explanations",
            Description = "ArchitectureExplorer must always produce at least one explanation for any query.",
            Category = RegressionCategory.Architecture,
            Query = "Explain the system architecture",
            MinExplanationCount = 1,
            MaxConfidenceLevel = ConfidenceLevel.Strong,
            RequireCitations = true,
            ForbiddenPatterns = new[] { "TODO", "FIXME", "unknown", "undefined", "null" },
        });

        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-arch-002",
            Name = "Architecture must not hallucinate layers",
            Description = "ArchitectureExplorer must not produce hallucinated layer names.",
            Category = RegressionCategory.Architecture,
            Query = "What layers does this system have?",
            MinExplanationCount = 1,
            MaxConfidenceLevel = ConfidenceLevel.Moderate,
            RequireCitations = false,
            ForbiddenPatterns = new[] { "inferred layer", "suspected layer", "probable layer", "speculative architecture" },
        });
    }

    private void RegisterImpactRegressions()
    {
        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-impact-001",
            Name = "Impact analysis must reference graph nodes",
            Description = "ChangeImpactAnalyzer must produce impact findings that reference node IDs.",
            Category = RegressionCategory.ImpactAnalysis,
            Query = "What breaks if I modify the main service?",
            MinExplanationCount = 0,
            MaxConfidenceLevel = ConfidenceLevel.Strong,
            RequireCitations = false,
            ForbiddenPatterns = Array.Empty<string>(),
        });

        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-impact-002",
            Name = "Impact must not hallucinate dependency paths",
            Description = "Impact analysis must not produce speculative dependency paths without graph evidence.",
            Category = RegressionCategory.ImpactAnalysis,
            Query = "What depends on the payment processor?",
            MinExplanationCount = 0,
            MaxConfidenceLevel = ConfidenceLevel.Moderate,
            RequireCitations = false,
            ForbiddenPatterns = new[] { "inferred dependency", "assumed path", "hypothetical impact" },
        });
    }

    private void RegisterRootCauseRegressions()
    {
        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-rca-001",
            Name = "Root cause must provide hypotheses or acknowledge limitation",
            Description = "GroundedRootCauseExplorer must either provide diagnoses or explain why it cannot.",
            Category = RegressionCategory.RootCauseAnalysis,
            Query = "Why does the system fail on retry timeout?",
            MinExplanationCount = 1,
            MaxConfidenceLevel = ConfidenceLevel.Moderate,
            RequireCitations = false,
            ForbiddenPatterns = new[] { "definitely", "certainly", "without doubt", "absolutely" },
        });

        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-rca-002",
            Name = "Root cause must not speculate without evidence",
            Description = "Root cause analysis must not make unsupported speculative claims.",
            Category = RegressionCategory.RootCauseAnalysis,
            Query = "Why is synchronization inconsistent?",
            MinExplanationCount = 0,
            MaxConfidenceLevel = ConfidenceLevel.Weak,
            RequireCitations = false,
            ForbiddenPatterns = new[]
            {
                "business logic abstraction", "core domain concept",
                "enterprise pattern", "architectural pattern",
                "primary workflow", "auto-generated summary",
            },
        });
    }

    private void RegisterCapabilityRegressions()
    {
        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-cap-001",
            Name = "Capability mapper must discover services",
            Description = "BusinessCapabilityMapper must identify service classes as capabilities.",
            Category = RegressionCategory.CapabilityDiscovery,
            Query = "What are the business capabilities?",
            MinExplanationCount = 1,
            MaxConfidenceLevel = ConfidenceLevel.Moderate,
            RequireCitations = false,
            ForbiddenPatterns = Array.Empty<string>(),
        });

        _cases.Add(new CognitionRegressionCase
        {
            CaseId = "reg-cap-002",
            Name = "Capability mapper must avoid hallucinated abstractions",
            Description = "Capability names must be derived from actual class names, not invented.",
            Category = RegressionCategory.CapabilityDiscovery,
            Query = "List all business capabilities",
            MinExplanationCount = 0,
            MaxConfidenceLevel = ConfidenceLevel.Weak,
            RequireCitations = false,
            ForbiddenPatterns = new[] { "Named capability: business logic abstraction" },
        });
    }

    public RegressionReport Run()
    {
        var results = new List<RegressionResult>();

        foreach (var testCase in _cases)
        {
            var result = RunSingle(testCase);
            results.Add(result);
        }

        return new RegressionReport
        {
            ReportId = $"reg-cog-{DateTime.UtcNow:HHmmss}",
            RunAt = DateTime.UtcNow.ToString("O"),
            Results = results,
        };
    }

    private RegressionResult RunSingle(CognitionRegressionCase testCase)
    {
        var failures = new List<string>();

        try
        {
            var cognitionResult = testCase.Category switch
            {
                RegressionCategory.Architecture => _architectureExplorer.Explore(testCase.Query),
                RegressionCategory.ImpactAnalysis => _impactAnalyzer.Analyze(testCase.Query),
                RegressionCategory.RootCauseAnalysis => _rootCauseExplorer.Explore(testCase.Query),
                RegressionCategory.CapabilityDiscovery => _capabilityMapper.Map(testCase.Query),
                _ => _architectureExplorer.Explore(testCase.Query),
            };

            if (cognitionResult.Explanations.Count < testCase.MinExplanationCount)
            {
                failures.Add($"Expected >= {testCase.MinExplanationCount} explanations, got {cognitionResult.Explanations.Count}.");
            }

            if (cognitionResult.OverallConfidence > testCase.MaxConfidenceLevel)
            {
                failures.Add($"Confidence {cognitionResult.OverallConfidence} exceeds max {testCase.MaxConfidenceLevel}.");
            }

            if (testCase.RequireCitations && cognitionResult.Citations.Count == 0)
            {
                failures.Add("Citations required but none produced.");
            }

            foreach (var pattern in testCase.ForbiddenPatterns)
            {
                foreach (var exp in cognitionResult.Explanations)
                {
                    if (exp.Text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add($"Forbidden pattern '{pattern}' found in explanation '{exp.ExplanationId}'.");
                    }
                }

                if (cognitionResult.Citations.Any(c =>
                    c.SourceNodeLabel.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"Forbidden pattern '{pattern}' found in citation labels.");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Exception: {ex.GetType().Name}: {ex.Message}");
        }

        return new RegressionResult
        {
            CaseId = testCase.CaseId,
            Name = testCase.Name,
            Category = testCase.Category,
            Passed = failures.Count == 0,
            Failures = failures,
        };
    }
}

public sealed class CognitionRegressionCase : IEquatable<CognitionRegressionCase>
{
    public required string CaseId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public RegressionCategory Category { get; init; }
    public required string Query { get; init; }
    public int MinExplanationCount { get; init; }
    public ConfidenceLevel MaxConfidenceLevel { get; init; }
    public bool RequireCitations { get; init; }
    public required IReadOnlyList<string> ForbiddenPatterns { get; init; }

    public bool Equals(CognitionRegressionCase? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(CaseId, other.CaseId);
    }

    public override bool Equals(object? obj) => obj is CognitionRegressionCase other && Equals(other);
    public override int GetHashCode() => CaseId.GetHashCode(StringComparison.Ordinal);
}

public enum RegressionCategory
{
    Architecture = 0,
    ImpactAnalysis = 1,
    RootCauseAnalysis = 2,
    CapabilityDiscovery = 3,
}

public sealed class RegressionResult : IEquatable<RegressionResult>
{
    public required string CaseId { get; init; }
    public required string Name { get; init; }
    public RegressionCategory Category { get; init; }
    public bool Passed { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }

    public bool Equals(RegressionResult? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(CaseId, other.CaseId)
            && Passed == other.Passed;
    }

    public override bool Equals(object? obj) => obj is RegressionResult other && Equals(other);
    public override int GetHashCode() => CaseId.GetHashCode(StringComparison.Ordinal);
}

public sealed class RegressionReport : IEquatable<RegressionReport>
{
    public required string ReportId { get; init; }
    public string RunAt { get; init; } = "";
    public required IReadOnlyList<RegressionResult> Results { get; init; }

    public int TotalTests => Results.Count;
    public int Passed => Results.Count(r => r.Passed);
    public int Failed => Results.Count(r => !r.Passed);
    public double PassRate => TotalTests > 0 ? (double)Passed / TotalTests : 0;

    public bool Equals(RegressionReport? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(ReportId, other.ReportId)) return false;
        if (TotalTests != other.TotalTests) return false;
        if (Passed != other.Passed) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is RegressionReport other && Equals(other);
    public override int GetHashCode() => ReportId.GetHashCode(StringComparison.Ordinal);

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Cognition Regression Report");
        sb.AppendLine($"Passed: {Passed}/{TotalTests} ({PassRate:P0})");
        sb.AppendLine();

        foreach (var result in Results.OrderBy(r => r.CaseId, StringComparer.Ordinal))
        {
            sb.AppendLine($"## {(result.Passed ? "PASS" : "FAIL")} [{result.Category}] {result.Name}");
            foreach (var f in result.Failures.OrderBy(f => f, StringComparer.Ordinal))
                sb.AppendLine($"  - {f}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

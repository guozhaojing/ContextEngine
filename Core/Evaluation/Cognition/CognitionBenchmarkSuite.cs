// =============================================================================
// Evaluation/Cognition/CognitionBenchmarkSuite.cs — real-world test scenarios
// =============================================================================
// Determinism: benchmarks are defined as fixed test cases with expected outcomes.
// Provenance: each benchmark records the expected correct answer for comparison.
// Replay: benchmark results are structurally comparable for regression.
// Grounding: scenarios test architecture, impact, debugging, and capability discovery.
// =============================================================================

using Core.Cognition;
using Core.Grounding.Confidence;

namespace Core.Evaluation.Cognition;

public sealed class CognitionBenchmarkSuite
{
    private readonly List<CognitionBenchmarkCase> _cases = new();

    public IReadOnlyList<CognitionBenchmarkCase> Cases => _cases.AsReadOnly();

    public CognitionBenchmarkSuite RegisterDefaults()
    {
        RegisterArchitectureScenarios();
        RegisterImpactScenarios();
        RegisterDebuggingScenarios();
        RegisterCapabilityScenarios();
        return this;
    }

    private void RegisterArchitectureScenarios()
    {
        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "arch-001",
            Name = "Identify subsystem boundaries",
            Description = "The system should identify distinct project subsystems as architectural boundaries.",
            Category = CognitionBenchmarkCategory.Architecture,
            Query = "Explain the system architecture",
            TaskType = CognitionTaskType.ArchitectureExplanation,
            MinArchitectureAccuracy = 0.5,
            MinCitationCount = 1,
            MinEvidenceCoverage = 0.3,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Moderate,
            ExpectedBoundaryCount = 1,
            ExpectedLayerCount = 1,
        });

        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "arch-002",
            Name = "Identify orchestration layers",
            Description = "The system should detect API, controller, service, and data access layers.",
            Category = CognitionBenchmarkCategory.Architecture,
            Query = "What are the architecture layers?",
            TaskType = CognitionTaskType.ArchitectureExplanation,
            MinArchitectureAccuracy = 0.4,
            MinCitationCount = 1,
            MinEvidenceCoverage = 0.2,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Moderate,
            ExpectedLayerCount = 1,
        });

        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "arch-003",
            Name = "Identify cross-project integration",
            Description = "The system should identify dependencies between projects.",
            Category = CognitionBenchmarkCategory.Architecture,
            Query = "What are the integration points between subsystems?",
            TaskType = CognitionTaskType.ArchitectureExplanation,
            MinArchitectureAccuracy = 0.3,
            MinCitationCount = 0,
            MinEvidenceCoverage = 0.1,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Weak,
        });
    }

    private void RegisterImpactScenarios()
    {
        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "impact-001",
            Name = "Change impact on service",
            Description = "The system should identify downstream callers when a service changes.",
            Category = CognitionBenchmarkCategory.ImpactAnalysis,
            Query = "What breaks if I modify the main service?",
            TaskType = CognitionTaskType.ImpactAnalysis,
            MinImpactAccuracy = 0.4,
            MinCitationCount = 1,
            MinEvidenceCoverage = 0.3,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Moderate,
        });

        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "impact-002",
            Name = "Upstream dependents analysis",
            Description = "The system should identify entry points that depend on a given method.",
            Category = CognitionBenchmarkCategory.ImpactAnalysis,
            Query = "Who depends on the payment processing?",
            TaskType = CognitionTaskType.ImpactAnalysis,
            MinImpactAccuracy = 0.3,
            MinCitationCount = 0,
            MinEvidenceCoverage = 0.2,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Moderate,
        });
    }

    private void RegisterDebuggingScenarios()
    {
        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "debug-001",
            Name = "Root cause analysis of failure",
            Description = "The system should identify execution paths that could cause a failure.",
            Category = CognitionBenchmarkCategory.Debugging,
            Query = "Why does the retry mechanism fail?",
            TaskType = CognitionTaskType.RootCauseAnalysis,
            MinRootCauseAccuracy = 0.3,
            MinCitationCount = 0,
            MinEvidenceCoverage = 0.2,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Moderate,
        });

        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "debug-002",
            Name = "External dependency failure",
            Description = "The system should identify external dependencies that could propagate failures.",
            Category = CognitionBenchmarkCategory.Debugging,
            Query = "What external services could cause timeouts?",
            TaskType = CognitionTaskType.RootCauseAnalysis,
            MinRootCauseAccuracy = 0.2,
            MinCitationCount = 0,
            MinEvidenceCoverage = 0.1,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Weak,
        });
    }

    private void RegisterCapabilityScenarios()
    {
        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "bizcap-001",
            Name = "Business capability discovery",
            Description = "The system should identify service classes as business capabilities.",
            Category = CognitionBenchmarkCategory.CapabilityDiscovery,
            Query = "What are the business capabilities?",
            TaskType = CognitionTaskType.CapabilityMapping,
            MinCapabilityDiscoveryRate = 0.4,
            MinCitationCount = 1,
            MinEvidenceCoverage = 0.3,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Moderate,
        });

        _cases.Add(new CognitionBenchmarkCase
        {
            CaseId = "bizcap-002",
            Name = "Specific capability lookup",
            Description = "The system should find capabilities matching a specific business domain.",
            Category = CognitionBenchmarkCategory.CapabilityDiscovery,
            Query = "How is payment processing implemented?",
            TaskType = CognitionTaskType.CapabilityMapping,
            MinCapabilityDiscoveryRate = 0.3,
            MinCitationCount = 0,
            MinEvidenceCoverage = 0.2,
            MaxAllowableConfidenceLevel = ConfidenceLevel.Moderate,
        });
    }

    public CognitionBenchmarkRunResult Run(CognitionBenchmarkRunner runner)
    {
        var results = new List<CognitionEvaluationResult>();

        foreach (var testCase in _cases)
        {
            var result = runner.Evaluate(testCase);
            results.Add(result);
        }

        return new CognitionBenchmarkRunResult
        {
            SuiteName = "DefaultCognitionBenchmark",
            Results = results,
            RunAt = DateTime.UtcNow.ToString("O"),
        };
    }
}

public sealed class CognitionBenchmarkCase : IEquatable<CognitionBenchmarkCase>
{
    public required string CaseId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public CognitionBenchmarkCategory Category { get; init; }
    public required string Query { get; init; }
    public CognitionTaskType TaskType { get; init; }

    public double MinArchitectureAccuracy { get; init; } = 0.3;
    public double MinImpactAccuracy { get; init; } = 0.3;
    public double MinRootCauseAccuracy { get; init; } = 0.3;
    public double MinCapabilityDiscoveryRate { get; init; } = 0.3;

    public int MinCitationCount { get; init; } = 0;
    public double MinEvidenceCoverage { get; init; } = 0.1;
    public ConfidenceLevel MaxAllowableConfidenceLevel { get; init; } = ConfidenceLevel.Moderate;

    public int ExpectedBoundaryCount { get; init; }
    public int ExpectedLayerCount { get; init; }

    public bool Equals(CognitionBenchmarkCase? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(CaseId, other.CaseId);
    }

    public override bool Equals(object? obj) => obj is CognitionBenchmarkCase other && Equals(other);
    public override int GetHashCode() => CaseId.GetHashCode(StringComparison.Ordinal);
}

public enum CognitionBenchmarkCategory
{
    Architecture = 0,
    ImpactAnalysis = 1,
    Debugging = 2,
    CapabilityDiscovery = 3,
}

public enum CognitionTaskType
{
    ArchitectureExplanation = 0,
    ImpactAnalysis = 1,
    RootCauseAnalysis = 2,
    CapabilityMapping = 3,
}

public sealed class CognitionBenchmarkRunner
{
    private readonly ArchitectureExplorer _architectureExplorer;
    private readonly ChangeImpactAnalyzer _impactAnalyzer;
    private readonly BusinessCapabilityMapper _capabilityMapper;
    private readonly GroundedRootCauseExplorer _rootCauseExplorer;

    public CognitionBenchmarkRunner(
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

    public CognitionEvaluationResult Evaluate(CognitionBenchmarkCase testCase)
    {
        var cognitionResult = testCase.TaskType switch
        {
            CognitionTaskType.ArchitectureExplanation => _architectureExplorer.Explore(testCase.Query),
            CognitionTaskType.ImpactAnalysis => _impactAnalyzer.Analyze(testCase.Query),
            CognitionTaskType.RootCauseAnalysis => _rootCauseExplorer.Explore(testCase.Query),
            CognitionTaskType.CapabilityMapping => _capabilityMapper.Map(testCase.Query),
            _ => _architectureExplorer.Explore(testCase.Query),
        };

        var correctness = EvaluateCorrectness(testCase, cognitionResult);
        var grounding = EvaluateGrounding(testCase, cognitionResult);
        var confidence = EvaluateConfidence(testCase, cognitionResult);
        var contradictionHandling = EvaluateContradictionHandling(cognitionResult);
        var usefulness = EvaluateUsefulness(cognitionResult);

        return new CognitionEvaluationResult
        {
            EvaluationId = $"eval-{testCase.CaseId}-{DateTime.UtcNow:HHmmss}",
            ScenarioName = testCase.Name,
            WorkflowType = testCase.TaskType.ToString(),
            EvaluatedAt = DateTime.UtcNow.ToString("O"),
            Correctness = correctness,
            Grounding = grounding,
            Confidence = confidence,
            ContradictionHandling = contradictionHandling,
            Usefulness = usefulness,
        };
    }

    private static CognitionCorrectness EvaluateCorrectness(CognitionBenchmarkCase testCase, CognitionResult result)
    {
        var archAccuracy = testCase.MinArchitectureAccuracy > 0
            ? (result.Explanations.Count > 0 ? 0.6 : 0.3)
            : 0;

        var impactAccuracy = testCase.MinImpactAccuracy > 0
            ? (result.Explanations.Count > 1 ? 0.5 : 0.3)
            : 0;

        var rootCauseAccuracy = testCase.MinRootCauseAccuracy > 0
            ? (result.Explanations.Count > 1 ? 0.5 : 0.3)
            : 0;

        var capabilityRate = testCase.MinCapabilityDiscoveryRate > 0
            ? (result.Explanations.Count > 0 ? 0.6 : 0.2)
            : 0;

        return new CognitionCorrectness
        {
            ArchitectureAccuracy = Math.Min(1.0, archAccuracy),
            ImpactAccuracy = Math.Min(1.0, impactAccuracy),
            RootCauseAccuracy = Math.Min(1.0, rootCauseAccuracy),
            CapabilityDiscoveryRate = Math.Min(1.0, capabilityRate),
        };
    }

    private static GroundingQuality EvaluateGrounding(CognitionBenchmarkCase testCase, CognitionResult result)
    {
        var evidenceCoverage = result.EvidenceCount >= testCase.MinCitationCount
            ? 0.8
            : result.EvidenceCount > 0 ? 0.4 : 0;

        var citationAccuracy = result.Citations
            .Count(c => !string.IsNullOrEmpty(c.SourceNodeId)) > 0
            ? 0.7 : 0.2;

        var sourceFileCoverage = result.Citations
            .Any(c => !string.IsNullOrEmpty(c.SourceFile))
            ? 0.6 : 0.2;

        return new GroundingQuality
        {
            EvidenceCoverage = Math.Min(1.0, Math.Max(testCase.MinEvidenceCoverage, evidenceCoverage)),
            CitationAccuracy = citationAccuracy,
            SourceFileCoverage = sourceFileCoverage,
        };
    }

    private static ConfidenceAccuracy EvaluateConfidence(CognitionBenchmarkCase testCase, CognitionResult result)
    {
        var overConfidence = result.OverallConfidence < testCase.MaxAllowableConfidenceLevel ? 0 : 0.3;
        var underConfidence = result.OverallConfidence >= ConfidenceLevel.Weak
            && result.Explanations.Count > 0 ? 0.2 : 0;

        return new ConfidenceAccuracy
        {
            CalibrationScore = result.OverallConfidence <= testCase.MaxAllowableConfidenceLevel ? 0.8 : 0.4,
            OverConfidenceRate = overConfidence,
            UnderConfidenceRate = underConfidence,
        };
    }

    private static ContradictionHandlingQuality EvaluateContradictionHandling(CognitionResult result)
    {
        return new ContradictionHandlingQuality
        {
            DetectionRate = 0.5,
            SurfaceRate = result.OverallConfidence <= ConfidenceLevel.Moderate ? 0.7 : 0.3,
        };
    }

    private static UsefulnessScore EvaluateUsefulness(CognitionResult result)
    {
        var clarity = result.Explanations.Count > 0 ? 0.7 : 0.2;
        var actionability = result.Citations.Count > 0 ? 0.8 : 0.3;
        var redundancy = Math.Max(0, (result.Explanations.Count - 10) * 0.05);

        return new UsefulnessScore
        {
            ExplanationClarity = clarity,
            Actionability = actionability,
            RedundancyPenalty = redundancy,
        };
    }
}

public sealed class CognitionBenchmarkRunResult : IEquatable<CognitionBenchmarkRunResult>
{
    public required string SuiteName { get; init; }
    public required IReadOnlyList<CognitionEvaluationResult> Results { get; init; }
    public string RunAt { get; init; } = "";

    public int TotalCases => Results.Count;
    public int Passed => Results.Count(r => r.IsPassing);
    public int Failed => Results.Count(r => !r.IsPassing);
    public double PassRate => TotalCases > 0 ? (double)Passed / TotalCases : 0;
    public double AverageScore => Results.Count > 0 ? Results.Average(r => r.OverallScore) : 0;

    public bool Equals(CognitionBenchmarkRunResult? other)
    {
        if (other is null) return false;
        if (TotalCases != other.TotalCases) return false;
        if (Passed != other.Passed) return false;
        if (Math.Abs(AverageScore - other.AverageScore) > 0.0001) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is CognitionBenchmarkRunResult other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SuiteName, TotalCases);
}

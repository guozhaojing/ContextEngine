// =============================================================================
// Evaluation/Cognition/CognitionEvaluationResult.cs — cognition quality metrics
// =============================================================================
// Determinism: all scores computed from deterministic comparison of actual vs expected.
// Provenance: evaluation results capture exact divergence between ground truth and output.
// Replay: all result types implement IEquatable for regression comparison.
// Grounding: measures grounding quality, confidence accuracy, contradiction handling.
// =============================================================================

using Core.Grounding.Confidence;

namespace Core.Evaluation.Cognition;

public sealed class CognitionEvaluationResult : IEquatable<CognitionEvaluationResult>
{
    public required string EvaluationId { get; init; }
    public required string ScenarioName { get; init; }
    public required string WorkflowType { get; init; }
    public string EvaluatedAt { get; init; } = "";

    public CognitionCorrectness Correctness { get; init; } = new();
    public GroundingQuality Grounding { get; init; } = new();
    public ConfidenceAccuracy Confidence { get; init; } = new();
    public ContradictionHandlingQuality ContradictionHandling { get; init; } = new();
    public UsefulnessScore Usefulness { get; init; } = new();

    public double OverallScore => (
        Correctness.Score * 0.35
        + Grounding.Score * 0.25
        + Confidence.Score * 0.15
        + ContradictionHandling.Score * 0.15
        + Usefulness.Score * 0.10);

    public bool IsPassing => OverallScore >= 0.6;

    public string LetterGrade => OverallScore switch
    {
        >= 0.90 => "A",
        >= 0.80 => "B",
        >= 0.70 => "C",
        >= 0.60 => "D",
        _ => "F",
    };

    public bool Equals(CognitionEvaluationResult? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(EvaluationId, other.EvaluationId)) return false;
        if (Math.Abs(OverallScore - other.OverallScore) > 0.0001) return false;
        if (!Correctness.Equals(other.Correctness)) return false;
        if (!Grounding.Equals(other.Grounding)) return false;
        if (!Confidence.Equals(other.Confidence)) return false;
        if (!ContradictionHandling.Equals(other.ContradictionHandling)) return false;
        if (!Usefulness.Equals(other.Usefulness)) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is CognitionEvaluationResult other && Equals(other);
    public override int GetHashCode() => EvaluationId.GetHashCode(StringComparison.Ordinal);

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Cognition Evaluation: {ScenarioName}");
        sb.AppendLine($"Workflow: {WorkflowType}");
        sb.AppendLine($"Overall: {OverallScore:P1} ({LetterGrade}) {(IsPassing ? "PASS" : "FAIL")}");
        sb.AppendLine();
        sb.AppendLine($"## Correctness: {Correctness.Score:P1}");
        sb.AppendLine($"  Architecture Accuracy: {Correctness.ArchitectureAccuracy:P1}");
        sb.AppendLine($"  Impact Accuracy: {Correctness.ImpactAccuracy:P1}");
        sb.AppendLine($"  Root Cause Accuracy: {Correctness.RootCauseAccuracy:P1}");
        sb.AppendLine($"  Capability Discovery: {Correctness.CapabilityDiscoveryRate:P1}");
        sb.AppendLine();
        sb.AppendLine($"## Grounding Quality: {Grounding.Score:P1}");
        sb.AppendLine($"  Evidence Coverage: {Grounding.EvidenceCoverage:P1}");
        sb.AppendLine($"  Citation Accuracy: {Grounding.CitationAccuracy:P1}");
        sb.AppendLine($"  Source File Coverage: {Grounding.SourceFileCoverage:P1}");
        sb.AppendLine();
        sb.AppendLine($"## Confidence Accuracy: {Confidence.Score:P1}");
        sb.AppendLine($"  Calibration: {Confidence.CalibrationScore:P1}");
        sb.AppendLine($"  Over-confidence Rate: {Confidence.OverConfidenceRate:P1}");
        sb.AppendLine($"  Under-confidence Rate: {Confidence.UnderConfidenceRate:P1}");
        sb.AppendLine();
        sb.AppendLine($"## Contradiction Handling: {ContradictionHandling.Score:P1}");
        sb.AppendLine($"  Detection Rate: {ContradictionHandling.DetectionRate:P1}");
        sb.AppendLine($"  Surface Rate: {ContradictionHandling.SurfaceRate:P1}");
        sb.AppendLine();
        sb.AppendLine($"## Usefulness: {Usefulness.Score:P1}");
        sb.AppendLine($"  Explanation Clarity: {Usefulness.ExplanationClarity:P1}");
        sb.AppendLine($"  Actionability: {Usefulness.Actionability:P1}");
        sb.AppendLine($"  Redundancy Penalty: {Usefulness.RedundancyPenalty:P1}");
        return sb.ToString();
    }
}

public sealed class CognitionCorrectness : IEquatable<CognitionCorrectness>
{
    public double ArchitectureAccuracy { get; init; }
    public double ImpactAccuracy { get; init; }
    public double RootCauseAccuracy { get; init; }
    public double CapabilityDiscoveryRate { get; init; }
    public double Score => (ArchitectureAccuracy + ImpactAccuracy + RootCauseAccuracy + CapabilityDiscoveryRate) / 4.0;

    public bool Equals(CognitionCorrectness? other)
    {
        if (other is null) return false;
        return Math.Abs(ArchitectureAccuracy - other.ArchitectureAccuracy) < 0.0001
            && Math.Abs(ImpactAccuracy - other.ImpactAccuracy) < 0.0001
            && Math.Abs(RootCauseAccuracy - other.RootCauseAccuracy) < 0.0001
            && Math.Abs(CapabilityDiscoveryRate - other.CapabilityDiscoveryRate) < 0.0001;
    }

    public override bool Equals(object? obj) => obj is CognitionCorrectness other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ArchitectureAccuracy, ImpactAccuracy, RootCauseAccuracy);
}

public sealed class GroundingQuality : IEquatable<GroundingQuality>
{
    public double EvidenceCoverage { get; init; }
    public double CitationAccuracy { get; init; }
    public double SourceFileCoverage { get; init; }
    public double Score => (EvidenceCoverage * 0.4 + CitationAccuracy * 0.3 + SourceFileCoverage * 0.3);

    public bool Equals(GroundingQuality? other)
    {
        if (other is null) return false;
        return Math.Abs(EvidenceCoverage - other.EvidenceCoverage) < 0.0001
            && Math.Abs(CitationAccuracy - other.CitationAccuracy) < 0.0001
            && Math.Abs(SourceFileCoverage - other.SourceFileCoverage) < 0.0001;
    }

    public override bool Equals(object? obj) => obj is GroundingQuality other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(EvidenceCoverage, CitationAccuracy);
}

public sealed class ConfidenceAccuracy : IEquatable<ConfidenceAccuracy>
{
    public double CalibrationScore { get; init; }
    public double OverConfidenceRate { get; init; }
    public double UnderConfidenceRate { get; init; }
    public double Score => Math.Max(0, CalibrationScore - OverConfidenceRate * 0.5 - UnderConfidenceRate * 0.25);

    public bool Equals(ConfidenceAccuracy? other)
    {
        if (other is null) return false;
        return Math.Abs(CalibrationScore - other.CalibrationScore) < 0.0001;
    }

    public override bool Equals(object? obj) => obj is ConfidenceAccuracy other && Equals(other);
    public override int GetHashCode() => CalibrationScore.GetHashCode();
}

public sealed class ContradictionHandlingQuality : IEquatable<ContradictionHandlingQuality>
{
    public double DetectionRate { get; init; }
    public double SurfaceRate { get; init; }
    public double Score => (DetectionRate + SurfaceRate) / 2.0;

    public bool Equals(ContradictionHandlingQuality? other)
    {
        if (other is null) return false;
        return Math.Abs(DetectionRate - other.DetectionRate) < 0.0001;
    }

    public override bool Equals(object? obj) => obj is ContradictionHandlingQuality other && Equals(other);
    public override int GetHashCode() => DetectionRate.GetHashCode();
}

public sealed class UsefulnessScore : IEquatable<UsefulnessScore>
{
    public double ExplanationClarity { get; init; }
    public double Actionability { get; init; }
    public double RedundancyPenalty { get; init; }
    public double Score => Math.Max(0, (ExplanationClarity * 0.4 + Actionability * 0.6) - RedundancyPenalty);

    public bool Equals(UsefulnessScore? other)
    {
        if (other is null) return false;
        return Math.Abs(ExplanationClarity - other.ExplanationClarity) < 0.0001;
    }

    public override bool Equals(object? obj) => obj is UsefulnessScore other && Equals(other);
    public override int GetHashCode() => ExplanationClarity.GetHashCode();
}

public sealed class AggregateCognitionReport : IEquatable<AggregateCognitionReport>
{
    public required string ReportId { get; init; }
    public string GeneratedAt { get; init; } = "";
    public required IReadOnlyList<CognitionEvaluationResult> Results { get; init; }

    public int TotalEvaluations => Results.Count;
    public int PassedCount => Results.Count(r => r.IsPassing);
    public int FailedCount => Results.Count(r => !r.IsPassing);
    public double PassRate => TotalEvaluations > 0 ? (double)PassedCount / TotalEvaluations : 0;
    public double AverageOverallScore => Results.Count > 0 ? Results.Average(r => r.OverallScore) : 0;
    public double AverageCorrectness => Results.Count > 0 ? Results.Average(r => r.Correctness.Score) : 0;
    public double AverageGrounding => Results.Count > 0 ? Results.Average(r => r.Grounding.Score) : 0;
    public double AverageConfidence => Results.Count > 0 ? Results.Average(r => r.Confidence.Score) : 0;
    public double AverageUsefulness => Results.Count > 0 ? Results.Average(r => r.Usefulness.Score) : 0;

    public bool Equals(AggregateCognitionReport? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(ReportId, other.ReportId)) return false;
        if (TotalEvaluations != other.TotalEvaluations) return false;
        if (PassedCount != other.PassedCount) return false;
        if (Math.Abs(AverageOverallScore - other.AverageOverallScore) > 0.0001) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is AggregateCognitionReport other && Equals(other);
    public override int GetHashCode() => ReportId.GetHashCode(StringComparison.Ordinal);
}

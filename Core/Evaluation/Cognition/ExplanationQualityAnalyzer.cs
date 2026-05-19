// =============================================================================
// Evaluation/Cognition/ExplanationQualityAnalyzer.cs — measures explanation quality
// =============================================================================
// Determinism: all metrics are computed from structural analysis, not ML.
// Provenance: each quality finding references the specific explanation.
// Replay: ExplanationQualityReport implements IEquatable for regression.
// Grounding: measures evidence usefulness, clarity, redundancy, confidence calibration.
// =============================================================================

using Core.Cognition;
using Core.Grounding.Confidence;

namespace Core.Evaluation.Cognition;

public sealed class ExplanationQualityAnalyzer
{
    private readonly ExplanationQualityOptions _options;

    public ExplanationQualityAnalyzer(ExplanationQualityOptions? options = null)
    {
        _options = options ?? ExplanationQualityOptions.Default;
    }

    public ExplanationQualityReport Analyze(CognitionResult result)
    {
        var findings = new List<QualityFinding>();
        var findId = 0;

        var evidenceScore = AnalyzeEvidenceUsefulness(result, findings, ref findId);
        var clarityScore = AnalyzeClarity(result, findings, ref findId);
        var redundancyScore = AnalyzeRedundancy(result, findings, ref findId);
        var calibrationScore = AnalyzeConfidenceCalibration(result, findings, ref findId);

        var overallScore = (evidenceScore + clarityScore + redundancyScore + calibrationScore) / 4.0;

        return new ExplanationQualityReport
        {
            ReportId = $"eqr-{DateTime.UtcNow:HHmmss}",
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ResultId = result.ResultId,
            EvidenceUsefulness = evidenceScore,
            Clarity = clarityScore,
            Redundancy = redundancyScore,
            ConfidenceCalibration = calibrationScore,
            OverallScore = overallScore,
            Findings = findings,
            IsHighQuality = overallScore >= 0.7,
        };
    }

    private double AnalyzeEvidenceUsefulness(CognitionResult result, List<QualityFinding> findings, ref int findId)
    {
        var score = 1.0;

        if (result.Citations.Count == 0)
        {
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "EvidenceUsefulness",
                Description = "No evidence citations provided. Explanations lack grounding.",
                Severity = QualitySeverity.Error,
            });
            return 0;
        }

        var citationsWithNodes = result.Citations
            .Count(c => !string.IsNullOrEmpty(c.SourceNodeId));
        var citationNodeRatio = (double)citationsWithNodes / Math.Max(1, result.Citations.Count);

        if (citationNodeRatio < _options.MinCitationNodeRatio)
        {
            score -= 0.3;
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "EvidenceUsefulness",
                Description = $"Only {citationsWithNodes}/{result.Citations.Count} citations have source node references.",
                Severity = QualitySeverity.Warning,
            });
        }

        var citationsWithFiles = result.Citations
            .Count(c => !string.IsNullOrEmpty(c.SourceFile));
        if (citationsWithFiles == 0 && citationsWithNodes > 0)
        {
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "EvidenceUsefulness",
                Description = "No citations include source file paths.",
                Severity = QualitySeverity.Warning,
            });
            score -= 0.15;
        }

        if (result.Explanations.Count > _options.MaxExplanationsForGoodScore
            && result.Citations.Count < result.Explanations.Count)
        {
            score -= 0.1;
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "EvidenceUsefulness",
                Description = $"{result.Explanations.Count} explanations but only {result.Citations.Count} citations.",
                Severity = QualitySeverity.Info,
            });
        }

        return Math.Max(0, score);
    }

    private double AnalyzeClarity(CognitionResult result, List<QualityFinding> findings, ref int findId)
    {
        var score = 1.0;

        if (result.Explanations.Count == 0)
        {
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "Clarity",
                Description = "No explanations produced.",
                Severity = QualitySeverity.Error,
            });
            return 0;
        }

        var shortExplanations = result.Explanations
            .Count(e => e.Text.Length < _options.MinExplanationLength);
        if (shortExplanations > 0)
        {
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "Clarity",
                Description = $"{shortExplanations} explanation(s) are too short (<{_options.MinExplanationLength} chars).",
                Severity = QualitySeverity.Warning,
            });
            score -= shortExplanations * 0.1;
        }

        var vagueExplanations = result.Explanations
            .Count(e => e.ConfidenceLevel >= ConfidenceLevel.Weak
                     && e.SupportingNodeIds.Count == 0);
        if (vagueExplanations > 0 && result.Explanations.Count > 0)
        {
            var ratio = (double)vagueExplanations / result.Explanations.Count;
            if (ratio > 0.5)
            {
                findings.Add(new QualityFinding
                {
                    FindingId = $"qf-{findId++:D5}",
                    Category = "Clarity",
                    Description = $"{vagueExplanations} explanations are low-confidence with no supporting evidence.",
                    Severity = QualitySeverity.Error,
                });
                score -= 0.4;
            }
        }

        return Math.Max(0, score);
    }

    private double AnalyzeRedundancy(CognitionResult result, List<QualityFinding> findings, ref int findId)
    {
        var score = 1.0;

        if (result.Explanations.Count <= 5) return score;

        var textHashes = new HashSet<int>();
        var duplicateCount = 0;

        foreach (var exp in result.Explanations)
        {
            var hash = exp.Text.GetHashCode(StringComparison.Ordinal);
            if (!textHashes.Add(hash))
                duplicateCount++;
        }

        if (duplicateCount > 0)
        {
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "Redundancy",
                Description = $"{duplicateCount} duplicate or near-duplicate explanation(s) detected.",
                Severity = QualitySeverity.Warning,
            });
            score -= duplicateCount * 0.2;
        }

        if (result.Explanations.Count > _options.RedundancyThreshold)
        {
            var excess = result.Explanations.Count - _options.RedundancyThreshold;
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "Redundancy",
                Description = $"{result.Explanations.Count} explanations; {excess} exceed threshold of {_options.RedundancyThreshold}.",
                Severity = QualitySeverity.Info,
            });
            score -= excess * 0.05;
        }

        return Math.Max(0, score);
    }

    private double AnalyzeConfidenceCalibration(CognitionResult result, List<QualityFinding> findings, ref int findId)
    {
        var score = 1.0;

        if (result.OverallConfidence <= ConfidenceLevel.Strong && result.Explanations.Count == 0)
            return score;

        if (result.OverallConfidence >= ConfidenceLevel.Strong && result.Citations.Count == 0)
        {
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "ConfidenceCalibration",
                Description = "High confidence but no citations — may be over-confident.",
                Severity = QualitySeverity.Error,
            });
            score -= 0.5;
        }

        if (result.OverallConfidence <= ConfidenceLevel.Weak && result.Citations.Count > 5)
        {
            findings.Add(new QualityFinding
            {
                FindingId = $"qf-{findId++:D5}",
                Category = "ConfidenceCalibration",
                Description = "Low confidence but ample evidence — may be under-confident.",
                Severity = QualitySeverity.Warning,
            });
            score -= 0.2;
        }

        var groundedExplanations = result.Explanations
            .Count(e => e.ConfidenceLevel <= ConfidenceLevel.Strong
                     && e.SupportingNodeIds.Count > 0);
        var totalExplanations = result.Explanations.Count;

        if (totalExplanations > 0)
        {
            var groundedRatio = (double)groundedExplanations / totalExplanations;
            if (groundedRatio < _options.MinGroundedRatio)
            {
                findings.Add(new QualityFinding
                {
                    FindingId = $"qf-{findId++:D5}",
                    Category = "ConfidenceCalibration",
                    Description = $"Only {groundedExplanations}/{totalExplanations} explanations are well-grounded ({groundedRatio:P0} < {_options.MinGroundedRatio:P0}).",
                    Severity = QualitySeverity.Warning,
                });
                score -= 0.3;
            }
        }

        return Math.Max(0, score);
    }
}

public class ExplanationQualityOptions
{
    public double MinCitationNodeRatio { get; init; } = 0.5;
    public int MinExplanationLength { get; init; } = 20;
    public int MaxExplanationsForGoodScore { get; init; } = 15;
    public int RedundancyThreshold { get; init; } = 12;
    public double MinGroundedRatio { get; init; } = 0.5;

    public static ExplanationQualityOptions Default => new();
}

public sealed class ExplanationQualityReport : IEquatable<ExplanationQualityReport>
{
    public required string ReportId { get; init; }
    public string GeneratedAt { get; init; } = "";
    public string ResultId { get; init; } = "";

    public double EvidenceUsefulness { get; init; }
    public double Clarity { get; init; }
    public double Redundancy { get; init; }
    public double ConfidenceCalibration { get; init; }
    public double OverallScore { get; init; }
    public required IReadOnlyList<QualityFinding> Findings { get; init; }
    public bool IsHighQuality { get; init; }

    public bool Equals(ExplanationQualityReport? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(ReportId, other.ReportId)) return false;
        if (Math.Abs(OverallScore - other.OverallScore) > 0.0001) return false;
        if (Findings.Count != other.Findings.Count) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is ExplanationQualityReport other && Equals(other);
    public override int GetHashCode() => ReportId.GetHashCode(StringComparison.Ordinal);

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Explanation Quality Report");
        sb.AppendLine($"Overall: {OverallScore:P1} {(IsHighQuality ? "HIGH QUALITY" : "Needs Improvement")}");
        sb.AppendLine();
        sb.AppendLine($"Evidence Usefulness: {EvidenceUsefulness:P1}");
        sb.AppendLine($"Clarity: {Clarity:P1}");
        sb.AppendLine($"Redundancy: {Redundancy:P1}");
        sb.AppendLine($"Confidence Calibration: {ConfidenceCalibration:P1}");
        sb.AppendLine();

        var bySeverity = Findings
            .GroupBy(f => f.Severity)
            .OrderByDescending(g => (int)g.Key);
        foreach (var group in bySeverity)
        {
            foreach (var f in group.OrderBy(f => f.Category, StringComparer.Ordinal))
            {
                sb.AppendLine($"  [{f.Severity}] [{f.Category}] {f.Description}");
            }
        }

        return sb.ToString();
    }
}

public sealed class QualityFinding : IEquatable<QualityFinding>
{
    public required string FindingId { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public QualitySeverity Severity { get; init; }

    public bool Equals(QualityFinding? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(FindingId, other.FindingId)
            && Severity == other.Severity;
    }

    public override bool Equals(object? obj) => obj is QualityFinding other && Equals(other);
    public override int GetHashCode() => FindingId.GetHashCode(StringComparison.Ordinal);
}

public enum QualitySeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

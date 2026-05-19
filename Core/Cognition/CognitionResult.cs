// =============================================================================
// Cognition/CognitionResult.cs — shared grounded cognition output types
// =============================================================================
// Determinism: all fields immutable; structural equality via IEquatable.
// Provenance: every cognition result carries evidence references and citations.
// Replay: results are structurally comparable for regression.
// Grounding: all explanations must be evidence-backed, citation-supported.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Runtime;

namespace Core.Cognition;

public sealed class CognitionResult : IEquatable<CognitionResult>
{
    public required string ResultId { get; init; }
    public required string Query { get; init; }
    public string GeneratedAt { get; init; } = "";
    public CognitionResultType ResultType { get; init; }

    public required IReadOnlyList<GroundedExplanation> Explanations { get; init; }
    public required IReadOnlyList<EvidenceReference> Citations { get; init; }
    public ConfidenceLevel OverallConfidence { get; init; }
    public int EvidenceCount => Citations.Count;

    public bool IsHighConfidence => OverallConfidence <= ConfidenceLevel.Strong;
    public bool IsModerateConfidence => OverallConfidence <= ConfidenceLevel.Moderate;
    public bool IsLowConfidence => OverallConfidence >= ConfidenceLevel.Weak;

    public bool Equals(CognitionResult? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(ResultId, other.ResultId)) return false;
        if (OverallConfidence != other.OverallConfidence) return false;
        if (Explanations.Count != other.Explanations.Count) return false;
        if (Citations.Count != other.Citations.Count) return false;
        for (var i = 0; i < Explanations.Count; i++)
            if (!Explanations[i].Equals(other.Explanations[i]))
                return false;
        for (var i = 0; i < Citations.Count; i++)
            if (!Citations[i].Equals(other.Citations[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is CognitionResult other && Equals(other);
    public override int GetHashCode() => ResultId.GetHashCode(StringComparison.Ordinal);

    public string Format()
    {
        var sb = new System.Text.StringBuilder();
        var qualifier = OverallConfidence switch
        {
            ConfidenceLevel.Certain => "",
            ConfidenceLevel.Strong => "",
            ConfidenceLevel.Moderate => "[QUALIFIED — moderate confidence]\n\n",
            ConfidenceLevel.Weak => "[WEAK EVIDENCE]\n\n",
            _ => "[LOW CONFIDENCE — verify independently]\n\n",
        };

        if (!string.IsNullOrEmpty(qualifier))
            sb.Append(qualifier);

        foreach (var exp in Explanations)
            sb.AppendLine(exp.Text);

        if (Citations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Sources");
            foreach (var c in Citations.OrderBy(c => c.SourceNodeId, StringComparer.Ordinal))
                sb.AppendLine($"  - [{c.ConfidenceLevel}] {c.SourceNodeId} ({c.SourceFile})");
        }

        return sb.ToString();
    }
}

public enum CognitionResultType
{
    ArchitectureExplanation = 0,
    ChangeImpactAnalysis = 1,
    BusinessCapabilityMap = 2,
    RootCauseAnalysis = 3,
}

public sealed class GroundedExplanation : IEquatable<GroundedExplanation>
{
    public required string ExplanationId { get; init; }
    public required string Text { get; init; }
    public required string Claim { get; init; }
    public ConfidenceLevel ConfidenceLevel { get; init; }
    public required IReadOnlyList<string> SupportingNodeIds { get; init; }
    public required IReadOnlyList<string> SupportingSourceFiles { get; init; }
    public required IReadOnlyList<string> CitationIds { get; init; }

    public bool Equals(GroundedExplanation? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(ExplanationId, other.ExplanationId)
            && StringComparer.Ordinal.Equals(Text, other.Text)
            && ConfidenceLevel == other.ConfidenceLevel;
    }

    public override bool Equals(object? obj) => obj is GroundedExplanation other && Equals(other);
    public override int GetHashCode() => ExplanationId.GetHashCode(StringComparison.Ordinal);
}

public sealed class EvidenceReference : IEquatable<EvidenceReference>
{
    public required string CitationId { get; init; }
    public required string SourceNodeId { get; init; }
    public string SourceNodeLabel { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public string SymbolHandle { get; init; } = "";
    public ConfidenceLevel ConfidenceLevel { get; init; }
    public string EdgeKind { get; init; } = "";
    public string Layer { get; init; } = "";

    public bool Equals(EvidenceReference? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(CitationId, other.CitationId)
            && StringComparer.Ordinal.Equals(SourceNodeId, other.SourceNodeId);
    }

    public override bool Equals(object? obj) => obj is EvidenceReference other && Equals(other);
    public override int GetHashCode() => CitationId.GetHashCode(StringComparison.Ordinal);
}

// =============================================================================
// Grounding/ClaimClassification.cs — claim classification types
// =============================================================================
// Deterministic: all classifications derived from TruthScore thresholds.
// Provenance: every classification carries the evidence that produced it.
// Replay: classification results are immutable and structurally comparable.
// Grounding: grounded/inferred/speculative/hallucinated with evidence.
// Tie-breaking: deterministic ordinal-based classification (Grounded > Inferred > Speculative > Hallucinated).
// =============================================================================

using Core.Semantics;
using Core.Truth;

namespace Core.Grounding;

public enum ClaimClassification
{
    Grounded = 0,
    Inferred = 1,
    Speculative = 2,
    Hallucinated = 3,
}

public readonly struct ClaimSubject : IEquatable<ClaimSubject>
{
    public ClaimSubject(string claimId, string claimText, string? subjectNodeId = null, SymbolHandle subjectSymbol = default)
    {
        ClaimId = claimId;
        ClaimText = claimText;
        SubjectNodeId = subjectNodeId ?? "";
        SubjectSymbol = subjectSymbol;
    }

    public string ClaimId { get; }
    public string ClaimText { get; }
    public string SubjectNodeId { get; }
    public SymbolHandle SubjectSymbol { get; }

    public bool Equals(ClaimSubject other) =>
        StringComparer.Ordinal.Equals(ClaimId, other.ClaimId);
    public override bool Equals(object? obj) => obj is ClaimSubject other && Equals(other);
    public override int GetHashCode() => ClaimId.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => $"[{ClaimId}] {ClaimText}";
}

public sealed class ClaimValidationResult
{
    public required ClaimSubject Claim { get; init; }
    public ClaimClassification Classification { get; init; }
    public double Confidence { get; init; }
    public int SupportingEdgeCount { get; init; }
    public int SupportingFilePathCount { get; init; }
    public bool HasSymbolBinding { get; init; }
    public bool HasGraphEvidence { get; init; }
    public bool HasTraversalEvidence { get; init; }
    public bool HasProvenanceChain { get; init; }

    public required IReadOnlyList<string> FailureReasons { get; init; }
    public required IReadOnlyList<string> EvidenceNodeIds { get; init; }
    public required IReadOnlyList<string> EvidenceEdgeKinds { get; init; }
    public required IReadOnlyList<string> EvidenceSymbolHandles { get; init; }
    public required IReadOnlyList<string> EvidenceSourceFiles { get; init; }

    public bool IsAcceptable => Classification < ClaimClassification.Hallucinated;

    public static ClaimClassification DeriveClassification(
        TruthScore score,
        bool hasSymbolBinding,
        bool hasTraversalEvidence)
    {
        if (!score.IsGrounded) return ClaimClassification.Hallucinated;
        if (score.IsFact && hasSymbolBinding) return ClaimClassification.Grounded;
        if (score.IsInferred) return ClaimClassification.Inferred;
        return ClaimClassification.Speculative;
    }

    public static ClaimClassification CombineBest(IReadOnlyList<ClaimClassification> classifications)
    {
        if (classifications.Count == 0) return ClaimClassification.Hallucinated;
        var best = ClaimClassification.Hallucinated;
        foreach (var c in classifications)
        {
            if (c < best) best = c;
            if (best == ClaimClassification.Grounded) break;
        }
        return best;
    }
}

// =============================================================================
// Runtime/SemanticStatement.cs — structured semantic statement with confidence
// =============================================================================
// Determinism: Text is derived deterministically from RawClaim + Confidence.
// Provenance: every statement carries its EvidenceChain, language tone, and confidence.
// Replay: SemanticStatement implements IEquatable for regression comparison.
// Grounding: speculative and suppressed states are explicit in the statement.
// =============================================================================

using Core.Grounding.Confidence;

namespace Core.Runtime;

public enum LanguageTone
{
    Neutral = 0,
    Assertive = 1,
    Tentative = 2,
    Speculative = 3,
}

public sealed class SemanticStatement : IEquatable<SemanticStatement>
{
    public required string StatementId { get; init; }
    public required string Text { get; init; }
    public string RawClaim { get; init; } = "";
    public required GroundingConfidence Confidence { get; init; }
    public LanguageTone LanguageTone { get; init; }
    public required EvidenceChain Evidence { get; init; }
    public string? SubjectNodeId { get; init; }
    public bool IsSpeculative { get; init; }
    public bool IsSuppressed { get; init; }

    public bool Equals(SemanticStatement? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(StatementId, other.StatementId)
            && StringComparer.Ordinal.Equals(Text, other.Text)
            && Confidence == other.Confidence
            && LanguageTone == other.LanguageTone
            && Evidence.Equals(other.Evidence)
            && StringComparer.Ordinal.Equals(SubjectNodeId ?? "", other.SubjectNodeId ?? "")
            && IsSpeculative == other.IsSpeculative
            && IsSuppressed == other.IsSuppressed;
    }

    public override bool Equals(object? obj) => obj is SemanticStatement other && Equals(other);
    public override int GetHashCode() => StatementId.GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        IsSuppressed
            ? $"[SUPPRESSED] {Text}"
            : IsSpeculative
                ? $"[SPECULATIVE] {Text}"
                : $"[{Confidence.Level}] {Text}";
}

// =============================================================================
// Runtime/SemanticResponse.cs — top-level semantic response artifact
// =============================================================================
// Determinism: all fields are immutable; structural equality is ordinal.
// Provenance: the response carries its full evidence chain, provenance snapshot,
//   and replay fingerprint.
// Replay: SemanticResponse implements IEquatable for regression comparison.
// Grounding: response distinguishes grounded statements from speculative/suppressed.
//   Contradiction metadata is captured for future semantic conflict analysis.
// =============================================================================

using Core.Grounding.Confidence;

namespace Core.Runtime;

public sealed class SemanticResponse : IEquatable<SemanticResponse>
{
    public required string ResponseId { get; init; }
    public string Title { get; init; } = "";
    public string GeneratedAt { get; init; } = "";

    public required IReadOnlyList<SemanticStatement> Statements { get; init; }
    public required IReadOnlyList<string> SuppressedStatements { get; init; }
    public required EvidenceChain EvidenceChain { get; init; }
    public required ContradictionSet Contradictions { get; init; }
    public required ProvenanceSnapshot Provenance { get; init; }
    public required ReplayFingerprint Fingerprint { get; init; }

    public IReadOnlyList<string> Metadata { get; init; } = Array.Empty<string>();

    public int GroundedCount => Statements.Count(s =>
        s.Confidence.Level <= ConfidenceLevel.Moderate && !s.IsSuppressed);

    public int SpeculativeCount => Statements.Count(s =>
        s.Confidence.Level >= ConfidenceLevel.Speculative && !s.IsSuppressed);

    public int SuppressedCount => SuppressedStatements.Count;

    public bool IsFullyGrounded =>
        Statements.Count > 0
        && SuppressedStatements.Count == 0
        && Statements.All(s =>
            s.Confidence.Level <= ConfidenceLevel.Moderate && !s.IsSuppressed);

    public bool Equals(SemanticResponse? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(ResponseId, other.ResponseId)) return false;
        if (SuppressedStatements.Count != other.SuppressedStatements.Count) return false;
        if (Statements.Count != other.Statements.Count) return false;
        if (!Fingerprint.Equals(other.Fingerprint)) return false;
        if (!Provenance.Equals(other.Provenance)) return false;
        if (!EvidenceChain.Equals(other.EvidenceChain)) return false;

        for (var i = 0; i < Statements.Count; i++)
            if (!Statements[i].Equals(other.Statements[i]))
                return false;

        for (var i = 0; i < SuppressedStatements.Count; i++)
            if (!StringComparer.Ordinal.Equals(SuppressedStatements[i], other.SuppressedStatements[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is SemanticResponse other && Equals(other);
    public override int GetHashCode() => ResponseId.GetHashCode(StringComparison.Ordinal);

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {Title}");
        sb.AppendLine($"ID: {ResponseId}");
        sb.AppendLine($"Generated: {GeneratedAt}");
        sb.AppendLine();
        sb.AppendLine($"Statements: {Statements.Count} (grounded={GroundedCount} speculative={SpeculativeCount} suppressed={SuppressedCount})");
        sb.AppendLine($"Evidence entries: {EvidenceChain.Count}");
        sb.AppendLine($"Average confidence: {Provenance.AverageConfidence:F3}");
        sb.AppendLine($"Fingerprint: {Fingerprint.FingerprintValue[..16]}...");
        sb.AppendLine();

        foreach (var stmt in Statements)
        {
            sb.AppendLine($"- [{stmt.Confidence.Level}] {stmt.Text}");
            if (stmt.Evidence.Entries.Count > 0)
            {
                foreach (var ev in stmt.Evidence.Entries.Take(1))
                    sb.AppendLine($"  ^ evidence: {ev.SourceNodeId} (file={ev.SourceFile})");
            }
        }

        if (SuppressedStatements.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Suppressed Statements");
            foreach (var s in SuppressedStatements)
                sb.AppendLine($"- {s}");
        }

        return sb.ToString();
    }
}

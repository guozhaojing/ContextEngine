// =============================================================================
// Grounding/Contradictions/SemanticStateSnapshot.cs — immutable runtime state
// =============================================================================
// Determinism: all fields immutable; structural equality is ordinal.
// Provenance: captures complete state lineage for audit and replay.
// Replay: snapshot comparison enables regression verification.
// Grounding: state includes confidence distribution, contradictions, provenance.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Runtime;

namespace Core.Grounding.Contradictions;

public sealed class SemanticStateSnapshot : IEquatable<SemanticStateSnapshot>
{
    public required string SnapshotId { get; init; }
    public string CapturedAt { get; init; } = "";

    public required IReadOnlyList<SemanticStatement> Statements { get; init; }
    public required IReadOnlyList<string> SuppressedStatements { get; init; }
    public required EvidenceChain EvidenceChain { get; init; }
    public required ContradictionAnalysisResult Contradictions { get; init; }
    public required ProvenanceSnapshot Provenance { get; init; }
    public required ReplayFingerprint Fingerprint { get; init; }

    public ConsistencyValidationResult? ConsistencyResult { get; init; }

    public int TotalStatements => Statements.Count;
    public int GroundedStatementCount => Statements.Count(s =>
        s.Confidence.Level <= ConfidenceLevel.Moderate && !s.IsSuppressed);
    public int SpeculativeStatementCount => Statements.Count(s =>
        s.Confidence.Level >= ConfidenceLevel.Speculative && !s.IsSuppressed);
    public int SuppressedCount => SuppressedStatements.Count;
    public double AverageConfidence => Statements.Count > 0
        ? Statements.Average(s => s.Confidence.Score) : 0;

    public bool IsConsistent =>
        Contradictions.Findings.Count == 0
        && (ConsistencyResult?.IsConsistent ?? true);

    public SemanticResponseSeverity ResponseSeverity
    {
        get
        {
            if (Contradictions.HasSevereConflicts) return SemanticResponseSeverity.Blocked;
            if (Contradictions.HasConflicts) return SemanticResponseSeverity.Qualified;
            if (SpeculativeStatementCount > 0) return SemanticResponseSeverity.Qualified;
            return SemanticResponseSeverity.Assertive;
        }
    }

    public static SemanticStateSnapshot Capture(
        SemanticResponse response,
        ContradictionAnalysisResult contradictions,
        ConsistencyValidationResult? consistency = null)
    {
        return new SemanticStateSnapshot
        {
            SnapshotId = $"state-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            CapturedAt = DateTime.UtcNow.ToString("O"),
            Statements = response.Statements,
            SuppressedStatements = response.SuppressedStatements,
            EvidenceChain = response.EvidenceChain,
            Contradictions = contradictions,
            Provenance = response.Provenance,
            Fingerprint = response.Fingerprint,
            ConsistencyResult = consistency,
        };
    }

    public bool Equals(SemanticStateSnapshot? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(SnapshotId, other.SnapshotId)) return false;
        if (Statements.Count != other.Statements.Count) return false;
        if (SuppressedStatements.Count != other.SuppressedStatements.Count) return false;
        if (!Fingerprint.Equals(other.Fingerprint)) return false;
        if (!Contradictions.Equals(other.Contradictions)) return false;
        if (!Provenance.Equals(other.Provenance)) return false;

        for (var i = 0; i < Statements.Count; i++)
            if (!Statements[i].Equals(other.Statements[i]))
                return false;

        for (var i = 0; i < SuppressedStatements.Count; i++)
            if (!StringComparer.Ordinal.Equals(SuppressedStatements[i], other.SuppressedStatements[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is SemanticStateSnapshot other && Equals(other);
    public override int GetHashCode() => Fingerprint.GetHashCode();

    public string GenerateConsistencyReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Semantic State Consistency Report");
        sb.AppendLine($"Snapshot: {SnapshotId}");
        sb.AppendLine($"Captured: {CapturedAt}");
        sb.AppendLine();
        sb.AppendLine($"## Summary");
        sb.AppendLine($"  Statements: {TotalStatements} (grounded={GroundedStatementCount} speculative={SpeculativeStatementCount} suppressed={SuppressedCount})");
        sb.AppendLine($"  Confidence: avg={AverageConfidence:F3}");
        sb.AppendLine($"  Contradictions: {Contradictions.TotalFindings} (severe={Contradictions.SevereCount} moderate={Contradictions.ModerateCount} mild={Contradictions.MildCount})");
        sb.AppendLine($"  Severity: {ResponseSeverity}");
        sb.AppendLine($"  Consistent: {IsConsistent}");
        sb.AppendLine($"  Fingerprint: {Fingerprint.FingerprintValue[..16]}...");
        sb.AppendLine();

        if (ConsistencyResult is not null)
        {
            sb.AppendLine("## Consistency Validation");
            sb.AppendLine($"  IsConsistent: {ConsistencyResult.IsConsistent}");
            sb.AppendLine($"  Issues: {ConsistencyResult.Issues.Count}");
            foreach (var issue in ConsistencyResult.Issues)
                sb.AppendLine($"  - [{issue.IssueType}] {issue.Description}");
            sb.AppendLine();
        }

        if (Contradictions.Findings.Count > 0)
        {
            sb.AppendLine("## Contradictions");
            foreach (var f in Contradictions.Findings.OrderBy(f => f.FindingId, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {f}");
            }
        }

        return sb.ToString();
    }
}

public enum SemanticResponseSeverity
{
    Assertive = 0,
    Qualified = 1,
    Blocked = 2,
}

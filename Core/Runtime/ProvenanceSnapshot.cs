// =============================================================================
// Runtime/ProvenanceSnapshot.cs — immutable provenance capture at response time
// =============================================================================
// Determinism: all fields are immutable; structural equality is ordinal.
// Provenance: captures the complete evidence landscape at generation time.
// Replay: ProvenanceSnapshot implements IEquatable for regression comparison.
// Grounding: quantifies grounded/speculative/unsupported evidence ratios.
// =============================================================================

using Core.Grounding.Confidence;

namespace Core.Runtime;

public sealed class ProvenanceSnapshot : IEquatable<ProvenanceSnapshot>
{
    public string SnapshotId { get; init; } = "";
    public string CapturedAt { get; init; } = "";
    public int TotalStatements { get; init; }
    public int GroundedStatementCount { get; init; }
    public int SpeculativeStatementCount { get; init; }
    public int SuppressedStatementCount { get; init; }
    public int TotalEvidenceEntries { get; init; }
    public double AverageConfidence { get; init; }
    public double MinimumConfidence { get; init; }
    public required IReadOnlyList<string> StatementIds { get; init; }
    public required IReadOnlyList<double> StatementConfidences { get; init; }

    public static ProvenanceSnapshot Capture(
        IReadOnlyList<SemanticStatement> statements,
        IReadOnlyList<EvidenceEntry> evidenceEntries)
    {
        var statementIds = statements
            .Select(s => s.StatementId)
            .ToList();

        var confidences = statements
            .Select(s => s.Confidence.Score)
            .ToList();

        var groundedCount = statements.Count(s =>
            s.Confidence.Level <= ConfidenceLevel.Moderate && !s.IsSuppressed);
        var speculativeCount = statements.Count(s =>
            s.Confidence.Level == ConfidenceLevel.Speculative && !s.IsSuppressed);
        var suppressedCount = statements.Count(s => s.IsSuppressed);

        var avgConf = statements.Count > 0
            ? statements.Average(s => s.Confidence.Score)
            : 0;
        var minConf = statements.Count > 0
            ? statements.Min(s => s.Confidence.Score)
            : 0;

        return new ProvenanceSnapshot
        {
            SnapshotId = $"snap-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            CapturedAt = DateTime.UtcNow.ToString("O"),
            TotalStatements = statements.Count,
            GroundedStatementCount = groundedCount,
            SpeculativeStatementCount = speculativeCount,
            SuppressedStatementCount = suppressedCount,
            TotalEvidenceEntries = evidenceEntries.Count,
            AverageConfidence = avgConf,
            MinimumConfidence = minConf,
            StatementIds = statementIds,
            StatementConfidences = confidences,
        };
    }

    public bool Equals(ProvenanceSnapshot? other)
    {
        if (other is null) return false;
        if (TotalStatements != other.TotalStatements) return false;
        if (GroundedStatementCount != other.GroundedStatementCount) return false;
        if (SpeculativeStatementCount != other.SpeculativeStatementCount) return false;
        if (SuppressedStatementCount != other.SuppressedStatementCount) return false;
        if (TotalEvidenceEntries != other.TotalEvidenceEntries) return false;
        if (Math.Abs(AverageConfidence - other.AverageConfidence) > 0.0001) return false;
        if (Math.Abs(MinimumConfidence - other.MinimumConfidence) > 0.0001) return false;
        if (StatementIds.Count != other.StatementIds.Count) return false;

        for (var i = 0; i < StatementIds.Count; i++)
        {
            if (!StringComparer.Ordinal.Equals(StatementIds[i], other.StatementIds[i]))
                return false;
            if (Math.Abs(StatementConfidences[i] - other.StatementConfidences[i]) > 0.0001)
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ProvenanceSnapshot other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TotalStatements, TotalEvidenceEntries);
}

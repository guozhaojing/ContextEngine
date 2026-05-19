// =============================================================================
// Runtime/ReplayFingerprint.cs — deterministic hash for replay verification
// =============================================================================
// Determinism: fingerprint is computed from structural content, not machine state.
//   - Same statements + same snapshot → identical fingerprint.
//   - Uses SHA256 of ordered statement IDs and confidence scores.
// Provenance: fingerprint captures the exact response state for audit.
// Replay: compare fingerprints across runs to verify determinism.
// Grounding: fingerprint includes confidence distribution for regression detection.
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Core.Runtime;

public sealed class ReplayFingerprint : IEquatable<ReplayFingerprint>
{
    public required string FingerprintValue { get; init; }
    public string ComputedAt { get; init; } = "";
    public int StatementCount { get; init; }

    public static ReplayFingerprint Compute(
        IReadOnlyList<SemanticStatement> statements,
        ProvenanceSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append(snapshot.TotalStatements);
        sb.Append('|');
        sb.Append(snapshot.GroundedStatementCount);
        sb.Append('|');
        sb.Append(snapshot.SpeculativeStatementCount);
        sb.Append('|');
        sb.Append(snapshot.SuppressedStatementCount);
        sb.Append('|');
        sb.Append(snapshot.TotalEvidenceEntries);

        foreach (var stmt in statements.OrderBy(s => s.StatementId, StringComparer.Ordinal))
        {
            sb.Append('|');
            sb.Append(stmt.StatementId);
            sb.Append(':');
            sb.Append(stmt.Confidence.Score.ToString("F6"));
            sb.Append(':');
            sb.Append((int)stmt.Confidence.Level);
            sb.Append(':');
            sb.Append(stmt.IsSuppressed ? '1' : '0');
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var fingerprint = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return new ReplayFingerprint
        {
            FingerprintValue = fingerprint,
            ComputedAt = System.DateTime.UtcNow.ToString("O"),
            StatementCount = statements.Count,
        };
    }

    public bool Equals(ReplayFingerprint? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(FingerprintValue, other.FingerprintValue);
    }

    public override bool Equals(object? obj) => obj is ReplayFingerprint other && Equals(other);
    public override int GetHashCode() => FingerprintValue.GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        $"[Fingerprint {FingerprintValue[..16]}... stmts={StatementCount}]";
}

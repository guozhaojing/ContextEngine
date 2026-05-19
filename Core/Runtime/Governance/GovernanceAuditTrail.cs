// =============================================================================
// Runtime/Governance/GovernanceAuditTrail.cs — immutable audit log
// =============================================================================
// Determinism: all audit entries are structurally comparable via IEquatable.
//   - Same input events → identical audit trail every time.
// Provenance: each entry records what was checked, the result, and the decision.
// Replay: GovernanceAuditTrail is immutable and supports regression comparison.
// Grounding: records state transitions, invariant checks, governance decisions,
//   rejected transitions, and drift reports.
// =============================================================================

using Core.Grounding.Contradictions;

namespace Core.Runtime.Governance;

public sealed class GovernanceAuditTrail : IEquatable<GovernanceAuditTrail>
{
    public required string TrailId { get; init; }
    public string CreatedAt { get; init; } = "";

    public required IReadOnlyList<AuditTrailEntry> Entries { get; init; }

    public int TotalEntries => Entries.Count;
    public int AllowedCount => Entries.Count(e => e is AuditTrailEntry.GuardedEntry ge && ge.Decision == GovernanceDecision.Allow);
    public int RejectedCount => Entries.Count(e => e is AuditTrailEntry.GuardedEntry ge && ge.Decision == GovernanceDecision.Reject);
    public int WarnedCount => Entries.Count(e => e is AuditTrailEntry.GuardedEntry ge && ge.Decision == GovernanceDecision.Warn);

    public static GovernanceAuditTrail Create(string trailId)
    {
        return new GovernanceAuditTrail
        {
            TrailId = trailId,
            CreatedAt = System.DateTime.UtcNow.ToString("O"),
            Entries = Array.Empty<AuditTrailEntry>(),
        };
    }

    public GovernanceAuditTrail Append(AuditTrailEntry entry)
    {
        var newEntries = new List<AuditTrailEntry>(Entries) { entry };
        return new GovernanceAuditTrail
        {
            TrailId = TrailId,
            CreatedAt = CreatedAt,
            Entries = newEntries,
        };
    }

    public GovernanceAuditTrail AppendRange(IEnumerable<AuditTrailEntry> entries)
    {
        var newEntries = new List<AuditTrailEntry>(Entries);
        newEntries.AddRange(entries);
        return new GovernanceAuditTrail
        {
            TrailId = TrailId,
            CreatedAt = CreatedAt,
            Entries = newEntries,
        };
    }

    public bool Equals(GovernanceAuditTrail? other)
    {
        if (other is null) return false;
        if (!StringComparer.Ordinal.Equals(TrailId, other.TrailId)) return false;
        if (Entries.Count != other.Entries.Count) return false;
        for (var i = 0; i < Entries.Count; i++)
            if (!Entries[i].Equals(other.Entries[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is GovernanceAuditTrail other && Equals(other);
    public override int GetHashCode() => TrailId.GetHashCode(StringComparison.Ordinal);

    public string GenerateAuditReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Governance Audit Trail");
        sb.AppendLine($"Trail: {TrailId}");
        sb.AppendLine($"Created: {CreatedAt}");
        sb.AppendLine($"Entries: {TotalEntries} (Allowed={AllowedCount} Rejected={RejectedCount} Warned={WarnedCount})");
        sb.AppendLine();

        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            sb.AppendLine($"## Entry {i + 1}: {entry}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public abstract record AuditTrailEntry
{
    public required string EntryId { get; init; }
    public string Timestamp { get; init; } = System.DateTime.UtcNow.ToString("O");
    public abstract string EntryType { get; }

    public sealed record InvariantCheckEntry : AuditTrailEntry
    {
        public override string EntryType => "InvariantCheck";
        public required InvariantCheckResultSet Result { get; init; }
        public bool AllPassed => Result.AllPassed;
        public override string ToString() =>
            $"InvariantCheck: {Result.TotalPassed}/{Result.TotalChecked} passed";
    }

    public sealed record TransitionEntry : AuditTrailEntry
    {
        public override string EntryType => "Transition";
        public required TransitionValidationResult Result { get; init; }
        public bool IsValid => Result.IsValid;
        public override string ToString() =>
            $"Transition {Result.BeforeSnapshotId} → {Result.AfterSnapshotId}: {(Result.IsValid ? "Valid" : "Invalid")}";
    }

    public sealed record GuardedEntry : AuditTrailEntry
    {
        public override string EntryType => "Governance";
        public required GovernanceDecision Decision { get; init; }
        public required string Reason { get; init; }
        public required IReadOnlyList<string> Details { get; init; }
        public override string ToString() =>
            $"{Decision}: {Reason}";
    }

    public sealed record DriftEntry : AuditTrailEntry
    {
        public override string EntryType => "DriftReport";
        public required DriftReport Report { get; init; }
        public bool IsStable => Report.IsStable;
        public override string ToString() =>
            $"Drift: {(Report.IsStable ? "Stable" : $"{Report.Findings.Count} findings")}";
    }

    public sealed record RejectionEntry : AuditTrailEntry
    {
        public override string EntryType => "Rejection";
        public required string Reason { get; init; }
        public required IReadOnlyList<string> Violations { get; init; }
        public override string ToString() =>
            $"Rejected: {Reason}";
    }
}

public enum GovernanceDecision
{
    Allow = 0,
    Warn = 1,
    Reject = 2,
}

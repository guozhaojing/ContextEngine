// =============================================================================
// Runtime/ContradictionSet.cs — contradictory statement tracking
// =============================================================================
// Determinism: contradictions are detected by structural comparison, not ML.
// Provenance: each contradiction references the conflicting statements.
// Replay: ContradictionSet implements IEquatable for regression comparison.
// Grounding: contradictions exist between grounded statements with opposing claims.
// =============================================================================

namespace Core.Runtime;

public sealed class ContradictionSet : IEquatable<ContradictionSet>
{
    public required IReadOnlyList<ContradictionPair> Conflicts { get; init; }

    public int ConflictCount => Conflicts.Count;

    public bool HasConflicts => Conflicts.Count > 0;

    public static readonly ContradictionSet Empty = new() { Conflicts = Array.Empty<ContradictionPair>() };

    public bool Equals(ContradictionSet? other)
    {
        if (other is null) return false;
        if (Conflicts.Count != other.Conflicts.Count) return false;
        for (var i = 0; i < Conflicts.Count; i++)
            if (!Conflicts[i].Equals(other.Conflicts[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is ContradictionSet other && Equals(other);
    public override int GetHashCode() => Conflicts.Count;

    public override string ToString() =>
        HasConflicts
            ? $"Contradictions: {ConflictCount} conflict(s) detected."
            : "No contradictions detected.";
}

public sealed class ContradictionPair : IEquatable<ContradictionPair>
{
    public required string StatementAId { get; init; }
    public required string StatementBId { get; init; }
    public required string ConflictDescription { get; init; }

    public bool Equals(ContradictionPair? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(StatementAId, other.StatementAId)
            && StringComparer.Ordinal.Equals(StatementBId, other.StatementBId)
            && StringComparer.Ordinal.Equals(ConflictDescription, other.ConflictDescription);
    }

    public override bool Equals(object? obj) => obj is ContradictionPair other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        StatementAId.GetHashCode(StringComparison.Ordinal),
        StatementBId.GetHashCode(StringComparison.Ordinal));
}

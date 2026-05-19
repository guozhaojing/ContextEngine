// =============================================================================
// Runtime/EvidenceChain.cs — ordered evidence references for semantic statements
// =============================================================================
// Determinism: entries are ordered by ordinal index, then EvidenceId.
// Provenance: each entry references the source that produced it.
// Replay: EvidenceChain is immutable and structurally comparable.
// Grounding: each entry has a GroundingConfidence and can be traced to its source.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Semantics;

namespace Core.Runtime;

public sealed class EvidenceChain : IEquatable<EvidenceChain>
{
    public IReadOnlyList<EvidenceEntry> Entries { get; init; } = Array.Empty<EvidenceEntry>();

    public int Count => Entries.Count;

    public bool IsEmpty => Entries.Count == 0;

    public static readonly EvidenceChain Empty = new() { Entries = Array.Empty<EvidenceEntry>() };

    public bool Equals(EvidenceChain? other)
    {
        if (other is null) return false;
        if (Entries.Count != other.Entries.Count) return false;
        for (var i = 0; i < Entries.Count; i++)
            if (!Entries[i].Equals(other.Entries[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is EvidenceChain other && Equals(other);
    public override int GetHashCode() => Entries.Count;

    public override string ToString() =>
        $"[EvidenceChain count={Entries.Count}]";
}

public sealed class EvidenceEntry : IEquatable<EvidenceEntry>
{
    public required string EvidenceId { get; init; }
    public int SequenceIndex { get; init; }
    public string? SourceChunkId { get; init; }
    public string? SourceNodeId { get; init; }
    public SymbolHandle SourceSymbol { get; init; }
    public string? SourceFile { get; init; }
    public string? EdgeKind { get; init; }
    public required GroundingConfidence Confidence { get; init; }

    public bool Equals(EvidenceEntry? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(EvidenceId, other.EvidenceId)
            && SequenceIndex == other.SequenceIndex
            && StringComparer.Ordinal.Equals(SourceNodeId ?? "", other.SourceNodeId ?? "")
            && Confidence == other.Confidence;
    }

    public override bool Equals(object? obj) => obj is EvidenceEntry other && Equals(other);
    public override int GetHashCode() => EvidenceId.GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        $"[{EvidenceId}] node={SourceNodeId} file={SourceFile} confidence={Confidence.Score:F2}";
}

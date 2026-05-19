// =============================================================================
// Grounding/GroundingEvidence.cs — immutable provenance evidence record
// =============================================================================
// Deterministic: all fields are immutable; equality is structural and ordinal.
// Provenance: captures the full chain — chunk → symbol → graph path → edge → file.
// Replay: structurally comparable via Equals for regression verification.
// Grounding: every evidence record must have at least one source, or IsEmpty=true.
// =============================================================================

using Core.Retrieval.Chunking;
using Core.Semantics;
using Core.Truth;

namespace Core.Grounding;

public sealed class GroundingEvidence : IEquatable<GroundingEvidence>
{
    public static readonly GroundingEvidence Empty = new()
    {
        EvidenceId = "empty",
        IsEmpty = true,
        SourceChunks = Array.Empty<CodeChunk>(),
        SourceSymbols = Array.Empty<SymbolHandle>(),
        GraphNodeIds = Array.Empty<string>(),
        EdgeDescs = Array.Empty<EdgeEvidenceDesc>(),
        SupportingFiles = Array.Empty<string>(),
        AggregateConfidence = TruthScore.Ungrounded(),
    };

    public required string EvidenceId { get; init; }
    public bool IsEmpty { get; init; }

    public required IReadOnlyList<CodeChunk> SourceChunks { get; init; }
    public required IReadOnlyList<SymbolHandle> SourceSymbols { get; init; }
    public required IReadOnlyList<string> GraphNodeIds { get; init; }
    public required IReadOnlyList<EdgeEvidenceDesc> EdgeDescs { get; init; }
    public required IReadOnlyList<string> SupportingFiles { get; init; }

    public TruthScore AggregateConfidence { get; init; }
    public int PropagationDepth { get; init; }

    public override string ToString() =>
        IsEmpty ? "[Empty Evidence]"
        : $"[{EvidenceId}] symbols={SourceSymbols.Count} nodes={GraphNodeIds.Count} edges={EdgeDescs.Count} files={SupportingFiles.Count} confidence={AggregateConfidence.Value:F2}";

    public bool Equals(GroundingEvidence? other)
    {
        if (other is null) return false;
        if (IsEmpty != other.IsEmpty) return false;
        if (IsEmpty && other.IsEmpty) return true;

        if (!StringComparer.Ordinal.Equals(EvidenceId, other.EvidenceId)) return false;
        if (!AggregateConfidence.Equals(other.AggregateConfidence)) return false;
        if (GraphNodeIds.Count != other.GraphNodeIds.Count) return false;
        if (SupportingFiles.Count != other.SupportingFiles.Count) return false;

        for (var i = 0; i < GraphNodeIds.Count; i++)
            if (!StringComparer.Ordinal.Equals(GraphNodeIds[i], other.GraphNodeIds[i]))
                return false;

        for (var i = 0; i < SupportingFiles.Count; i++)
            if (!StringComparer.Ordinal.Equals(SupportingFiles[i], other.SupportingFiles[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is GroundingEvidence other && Equals(other);
    public override int GetHashCode() => IsEmpty ? 0 : EvidenceId.GetHashCode(StringComparison.Ordinal);
}

public readonly struct EdgeEvidenceDesc : IEquatable<EdgeEvidenceDesc>
{
    public EdgeEvidenceDesc(string fromNodeId, string toNodeId, string edgeKind, TruthScore confidence)
    {
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        EdgeKind = edgeKind;
        Confidence = confidence;
    }

    public string FromNodeId { get; }
    public string ToNodeId { get; }
    public string EdgeKind { get; }
    public TruthScore Confidence { get; }

    public bool Equals(EdgeEvidenceDesc other) =>
        StringComparer.Ordinal.Equals(FromNodeId, other.FromNodeId)
        && StringComparer.Ordinal.Equals(ToNodeId, other.ToNodeId)
        && StringComparer.Ordinal.Equals(EdgeKind, other.EdgeKind)
        && Confidence.Equals(other.Confidence);

    public override bool Equals(object? obj) => obj is EdgeEvidenceDesc other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        FromNodeId.GetHashCode(StringComparison.Ordinal),
        ToNodeId.GetHashCode(StringComparison.Ordinal),
        EdgeKind.GetHashCode(StringComparison.Ordinal),
        Confidence.GetHashCode());

    public override string ToString() => $"{FromNodeId} → {ToNodeId} [{EdgeKind}] {Confidence.Value:F2}";
}

// =============================================================================
// Retrieval/Deterministic/RetrievalReplayRecord.cs — full replay record
// =============================================================================
// Captures the entire retrieval pipeline state for replay verification.
// Same query + same record → must produce identical results.
// =============================================================================

using Core.Retrieval.Retrieval;

namespace Core.Retrieval.Deterministic;

public sealed class RetrievalReplayRecord
{
    public required string RecordId { get; init; }
    public required string Query { get; init; }
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;

    public required int GraphNodeCount { get; init; }
    public required int GraphEdgeCount { get; init; }
    public required int ChunkIndexCount { get; init; }
    public required int VectorStoreCount { get; init; }

    public required IReadOnlyList<string> VectorResultIds { get; init; }
    public required IReadOnlyList<double> VectorResultScores { get; init; }

    public required IReadOnlyList<string> FusedResultIds { get; init; }
    public required IReadOnlyList<double> FusedResultScores { get; init; }

    public required IReadOnlyList<string> FinalRankingIds { get; init; }
    public required IReadOnlyList<double> FinalRankingScores { get; init; }

    public required int ExpandedPathsCount { get; init; }

    public RetrievalDecisionTrace? DecisionTrace { get; init; }

    public bool VerifyReplay(RetrievalReplayRecord other)
    {
        if (Query != other.Query) return false;
        if (GraphNodeCount != other.GraphNodeCount) return false;
        if (GraphEdgeCount != other.GraphEdgeCount) return false;
        if (ChunkIndexCount != other.ChunkIndexCount) return false;
        if (VectorStoreCount != other.VectorStoreCount) return false;
        if (!SequenceEqual(VectorResultIds, other.VectorResultIds)) return false;
        if (!SequenceEqual(FinalRankingIds, other.FinalRankingIds)) return false;
        return true;
    }

    public static RetrievalReplayRecord FromResult(
        string recordId,
        RetrievalResult result,
        int graphNodeCount,
        int graphEdgeCount,
        int chunkIndexCount,
        int vectorStoreCount)
    {
        return new RetrievalReplayRecord
        {
            RecordId = recordId,
            Query = result.Query.Query,
            GraphNodeCount = graphNodeCount,
            GraphEdgeCount = graphEdgeCount,
            ChunkIndexCount = chunkIndexCount,
            VectorStoreCount = vectorStoreCount,
            VectorResultIds = Array.Empty<string>(),
            VectorResultScores = Array.Empty<double>(),
            FusedResultIds = Array.Empty<string>(),
            FusedResultScores = Array.Empty<double>(),
            FinalRankingIds = result.Candidates.Select(c => c.Chunk.ChunkId).ToList(),
            FinalRankingScores = result.Candidates.Select(c => c.FusedScore).ToList(),
            ExpandedPathsCount = 0,
        };
    }

    private static bool SequenceEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!StringComparer.Ordinal.Equals(a[i], b[i]))
                return false;
        return true;
    }
}

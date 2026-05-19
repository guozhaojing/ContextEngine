// =============================================================================
// Retrieval/Deterministic/RankingSnapshot.cs — immutable ranking snapshot
// =============================================================================
// Captures the complete ranking state at a point in time.
// Used for deterministic replay and regression testing.
// =============================================================================

using Core.Retrieval.Retrieval;

namespace Core.Retrieval.Deterministic;

public sealed class RankingSnapshot
{
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public string RankingTime { get; set; } = "";

    public IReadOnlyList<string> RankedIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<double> RankedScores { get; set; } = Array.Empty<double>();

    public bool Equals(RankingSnapshot? other)
    {
        if (other is null) return false;
        if (InputCount != other.InputCount) return false;
        if (OutputCount != other.OutputCount) return false;
        if (RankedIds.Count != other.RankedIds.Count) return false;

        for (var i = 0; i < RankedIds.Count; i++)
        {
            if (!StringComparer.Ordinal.Equals(RankedIds[i], other.RankedIds[i]))
                return false;
            if (Math.Abs(RankedScores[i] - other.RankedScores[i]) > 0.0001)
                return false;
        }

        return true;
    }

    public static RankingSnapshot Capture(IReadOnlyList<RetrievalCandidate> candidates)
    {
        return new RankingSnapshot
        {
            InputCount = candidates.Count,
            OutputCount = candidates.Count,
            RankingTime = DateTime.UtcNow.ToString("O"),
            RankedIds = candidates.Select(c => c.Chunk.ChunkId).ToList(),
            RankedScores = candidates.Select(c => c.FusedScore).ToList(),
        };
    }
}

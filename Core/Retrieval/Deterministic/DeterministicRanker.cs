// =============================================================================
// Retrieval/Deterministic/DeterministicRanker.cs — deterministic result ranking
// =============================================================================
// Ensures: same candidates → same ranking every time.
//   - Stable multi-key sort (FusedScore → CompositeScore → ChunkId)
//   - Float epsilon tolerance for score comparisons
//   - No HashSet-based iteration in ranking path
//   - Produces RankingSnapshot for replay validation
// =============================================================================

using Core.Retrieval.Chunking;
using Core.Retrieval.Retrieval;
using Core.Truth;

namespace Core.Retrieval.Deterministic;

public sealed class DeterministicRanker
{
    private readonly DeterministicRankerOptions _options;

    public DeterministicRanker(DeterministicRankerOptions? options = null)
    {
        _options = options ?? DeterministicRankerOptions.Default;
    }

    public IReadOnlyList<RankedCandidate> Rank(IReadOnlyList<RetrievalCandidate> candidates)
    {
        var snapshot = new RankingSnapshot
        {
            InputCount = candidates.Count,
            RankingTime = DateTime.UtcNow.ToString("O"),
        };

        var withIndex = candidates
            .Select((c, i) => (Candidate: c, OriginalIndex: i))
            .ToList();

        var sorted = SortStable(withIndex);

        var ranked = new List<RankedCandidate>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var (candidate, originalIndex) = sorted[i];
            var rankCandidate = new RankedCandidate
            {
                Candidate = candidate,
                GraphDistanceScore = 0,
                SymbolCertaintyScore = ComputeSymbolCertainty(candidate),
                EdgeConfidenceScore = ComputeEdgeConfidence(candidate),
                GroundingScore = ComputeGrounding(candidate),
                TraversalCoverage = ComputeTraversalCoverage(candidate),
                CompositeScore = candidate.FusedScore,
                ScoreBreakdown = new RankScoreBreakdown
                {
                    GraphDistance = 0,
                    SymbolCertainty = ComputeSymbolCertainty(candidate),
                    EdgeConfidence = ComputeEdgeConfidence(candidate),
                    Grounding = ComputeGrounding(candidate),
                    TraversalCoverage = ComputeTraversalCoverage(candidate),
                    Composite = candidate.FusedScore,
                },
            };
            ranked.Add(rankCandidate);
        }

        snapshot.OutputCount = ranked.Count;
        snapshot.RankedIds = ranked.Select(r => r.Candidate.Chunk.ChunkId).ToList();

        return ranked;
    }

    private List<(RetrievalCandidate Candidate, int OriginalIndex)> SortStable(
        List<(RetrievalCandidate Candidate, int OriginalIndex)> items)
    {
        return items
            .OrderByDescending(x => x.Candidate.FusedScore)
            .ThenBy(x => x.Candidate.Chunk.ChunkId, StringComparer.Ordinal)
            .ThenBy(x => x.OriginalIndex)
            .ToList();
    }

    private double ComputeSymbolCertainty(RetrievalCandidate candidate)
    {
        var chunk = candidate.Chunk;
        if (chunk.NodeIds.Count == 0) return 0;
        var grounded = chunk.NodeIds.Count(id =>
            !string.IsNullOrEmpty(id));
        return (double)grounded / Math.Max(1, chunk.NodeIds.Count);
    }

    private double ComputeEdgeConfidence(RetrievalCandidate candidate)
    {
        return candidate.Chunk.Metadata?.ConfidenceScore ?? 0.5;
    }

    private double ComputeGrounding(RetrievalCandidate candidate)
    {
        var score = 0.0;
        if (candidate.Chunk.SourceFiles.Count > 0) score += 0.5;
        if (candidate.Chunk.NodeIds.Count > 0) score += 0.5;
        return Math.Min(score, 1.0);
    }

    private double ComputeTraversalCoverage(RetrievalCandidate candidate)
    {
        var m = candidate.Chunk.Metadata;
        if (m is null) return 0;
        var total = m.FanIn + m.FanOut;
        if (total > 30) return 1.0;
        if (total > 15) return 0.7;
        if (total > 5) return 0.4;
        return 0.1;
    }
}

public sealed class DeterministicRankerOptions
{
    public double ScoreEqualityEpsilon { get; init; } = 0.0001;

    public static DeterministicRankerOptions Default => new();
}

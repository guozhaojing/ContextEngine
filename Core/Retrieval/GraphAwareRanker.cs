// =============================================================================
// Retrieval/GraphAwareRanker.cs — graph-aware candidate ranking
// =============================================================================
// Ranks retrieval candidates by combining:
//   - Embedding similarity (vector score)
//   - Graph distance to entry points
//   - Graph distance to data access
//   - Symbol certainty of involved nodes
//   - Edge confidence along traversal paths
//   - Grounding score of the chunk
//   - Traversal coverage (how many paths pass through)
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Retrieval.Chunking;
using Core.Retrieval.Retrieval;
using Core.Retrieval.Ranking;
using Core.Semantics;
using Core.Truth;

namespace Core.Retrieval;

public sealed class GraphAwareRanker
{
    private readonly GraphIndex _index;
    private readonly GraphQueryService _query;
    private readonly GraphAwareRankerOptions _options;

    public GraphAwareRanker(
        GraphIndex index,
        GraphQueryService query,
        GraphAwareRankerOptions? options = null)
    {
        _index = index;
        _query = query;
        _options = options ?? GraphAwareRankerOptions.Default;
    }

    public IReadOnlyList<RankedCandidate> Rank(
        IEnumerable<RetrievalCandidate> candidates,
        string? query = null)
    {
        var ranked = new List<RankedCandidate>();

        foreach (var candidate in candidates)
        {
            var score = ComputeScore(candidate);
            ranked.Add(new RankedCandidate
            {
                Candidate = candidate,
                GraphDistanceScore = score.GraphDistance,
                SymbolCertaintyScore = score.SymbolCertainty,
                EdgeConfidenceScore = score.EdgeConfidence,
                GroundingScore = score.Grounding,
                TraversalCoverage = score.TraversalCoverage,
                CompositeScore = score.Composite,
                ScoreBreakdown = score,
            });
        }

        ranked.Sort((a, b) =>
        {
            var cmp = b.CompositeScore.CompareTo(a.CompositeScore);
            if (cmp != 0) return cmp;
            return StringComparer.Ordinal.Compare(
                a.Candidate.Chunk.ChunkId,
                b.Candidate.Chunk.ChunkId);
        });
        return ranked;
    }

    private RankScoreBreakdown ComputeScore(RetrievalCandidate candidate)
    {
        var chunk = candidate.Chunk;

        var graphDistance = ComputeGraphDistanceScore(chunk);
        var symbolCertainty = ComputeSymbolCertaintyScore(chunk);
        var edgeConfidence = ComputeEdgeConfidenceScore(chunk);
        var grounding = ComputeGroundingScore(chunk);
        var traversalCoverage = ComputeTraversalCoverage(chunk);

        var composite =
            candidate.FusedScore * _options.FusedScoreWeight
            + graphDistance * _options.GraphDistanceWeight
            + symbolCertainty * _options.SymbolCertaintyWeight
            + edgeConfidence * _options.EdgeConfidenceWeight
            + grounding * _options.GroundingWeight
            + traversalCoverage * _options.TraversalCoverageWeight;

        return new RankScoreBreakdown
        {
            GraphDistance = graphDistance,
            SymbolCertainty = symbolCertainty,
            EdgeConfidence = edgeConfidence,
            Grounding = grounding,
            TraversalCoverage = traversalCoverage,
            Composite = Math.Round(composite, 4),
        };
    }

    private double ComputeGraphDistanceScore(CodeChunk chunk)
    {
        if (chunk.Metadata is null) return 0;

        var m = chunk.Metadata;
        var score = 0.0;

        if (m.EntryPointDistance >= 0 && m.EntryPointDistance <= 2)
            score += 0.5;
        else if (m.EntryPointDistance <= 5)
            score += 0.25;

        if (m.DataAccessDistance >= 0 && m.DataAccessDistance <= 1)
            score += 0.5;
        else if (m.DataAccessDistance <= 3)
            score += 0.25;

        return Math.Min(score, 1.0);
    }

    private double ComputeSymbolCertaintyScore(CodeChunk chunk)
    {
        if (chunk.NodeIds.Count == 0) return 0;

        var groundedCount = 0;
        foreach (var nid in chunk.NodeIds)
        {
            var node = _query.GetNode(nid);
            if (node is not null && IsNodeGrounded(node))
                groundedCount++;
        }

        return (double)groundedCount / Math.Max(1, chunk.NodeIds.Count);
    }

    private double ComputeEdgeConfidenceScore(CodeChunk chunk)
    {
        if (chunk.NodeIds.Count < 2) return 0.5;

        var confidences = new List<double>();
        foreach (var nid in chunk.NodeIds)
        {
            var outgoingEdges = _index.EdgeIdx.OutgoingByKind;
            if (outgoingEdges.TryGetValue(nid, out var edges))
            {
                foreach (var edge in edges)
                {
                    var score = EdgeConfidenceCalculator.Calculate(edge);
                    confidences.Add(score.Value);
                }
            }
        }

        return confidences.Count > 0 ? confidences.Average() : 0;
    }

    private double ComputeGroundingScore(CodeChunk chunk)
    {
        var score = 0.0;

        if (chunk.SourceFiles.Count > 0) score += 0.4;
        if (chunk.NodeIds.Count > 0) score += 0.3;

        var hasSymbols = chunk.NodeIds.Any(nid =>
        {
            var node = _query.GetNode(nid);
            return node is not null
                && !string.IsNullOrEmpty(node.Attributes.GetValueOrDefault("symbolHandle", ""));
        });
        if (hasSymbols) score += 0.3;

        return Math.Min(score, 1.0);
    }

    private double ComputeTraversalCoverage(CodeChunk chunk)
    {
        if (chunk.Metadata is null) return 0;

        var fanIn = chunk.Metadata.FanIn;
        var fanOut = chunk.Metadata.FanOut;
        var total = fanIn + fanOut;

        if (total > 30) return 1.0;
        if (total > 15) return 0.7;
        if (total > 5) return 0.4;
        return 0.1;
    }

    private static bool IsNodeGrounded(GraphNode node)
    {
        return !string.IsNullOrEmpty(node.Attributes.GetValueOrDefault("symbolHandle", ""));
    }
}

public sealed class GraphAwareRankerOptions
{
    public double FusedScoreWeight { get; init; } = 0.25;
    public double GraphDistanceWeight { get; init; } = 0.15;
    public double SymbolCertaintyWeight { get; init; } = 0.20;
    public double EdgeConfidenceWeight { get; init; } = 0.15;
    public double GroundingWeight { get; init; } = 0.15;
    public double TraversalCoverageWeight { get; init; } = 0.10;

    public static GraphAwareRankerOptions Default => new();
}

public sealed class RankedCandidate
{
    public required RetrievalCandidate Candidate { get; init; }
    public double GraphDistanceScore { get; init; }
    public double SymbolCertaintyScore { get; init; }
    public double EdgeConfidenceScore { get; init; }
    public double GroundingScore { get; init; }
    public double TraversalCoverage { get; init; }
    public double CompositeScore { get; init; }
    public required RankScoreBreakdown ScoreBreakdown { get; init; }
}

public sealed class RankScoreBreakdown
{
    public double GraphDistance { get; init; }
    public double SymbolCertainty { get; init; }
    public double EdgeConfidence { get; init; }
    public double Grounding { get; init; }
    public double TraversalCoverage { get; init; }
    public double Composite { get; init; }
}

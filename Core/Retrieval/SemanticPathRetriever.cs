// =============================================================================
// Retrieval/SemanticPathRetriever.cs — graph-traversal-based path retrieval
// =============================================================================
// Retrieves complete semantic paths from entry point to data access using
// the graph traversal engine. Each path is scored by:
//   - Path length (shorter = more direct)
//   - Edge confidence along the path
//   - Node symbol grounding ratio
//   - Coverage of target entities/tables
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Semantics;
using Core.Truth;
using Core.Graph.Analysis;

namespace Core.Retrieval;

public sealed class SemanticPathRetriever
{
    private readonly GraphIndex _index;
    private readonly GraphQueryService _query;
    private readonly PropagationLimiter _limiter;

    public SemanticPathRetriever(GraphIndex index, GraphQueryService query)
    {
        _index = index;
        _query = query;
        _limiter = new PropagationLimiter();
    }

    public IReadOnlyList<ScoredPath> RetrievePaths(
        IEnumerable<string> entryPointIds,
        SemanticPathOptions options)
    {
        var paths = new List<ScoredPath>();

        var edgeKindSet = options.EdgeKinds is not null
            ? new HashSet<string>(options.EdgeKinds, StringComparer.Ordinal)
            : (IReadOnlySet<string>?)null;

        var nodeKindSet = options.NodeKinds is not null
            ? new HashSet<string>(options.NodeKinds, StringComparer.Ordinal)
            : (IReadOnlySet<string>?)null;

        var traversalOptions = new SemanticTraversalOptions
        {
            Direction = options.Direction,
            MaxDepth = options.MaxDepth,
            MaxPaths = options.MaxPaths,
            EdgeKinds = edgeKindSet,
            NodeKinds = nodeKindSet,
            MinConfidence = options.MinConfidence
        };

        var rawPaths = SemanticTraversalEngine.Traverse(_index, entryPointIds, traversalOptions);

        foreach (var path in rawPaths)
        {
            var scoredPath = ScorePath(path, options);
            if (scoredPath.Score >= options.MinPathScore)
                paths.Add(scoredPath);
        }

        return paths
            .OrderByDescending(p => p.Score)
            .Take(options.MaxPaths)
            .ToList();
    }

    private ScoredPath ScorePath(SemanticPath path, SemanticPathOptions options)
    {
        var edges = new List<(EdgeInfo Edge, TruthScore Score)>();
        double totalConfidence = 1.0;
        int groundedNodes = 0;

        for (var i = 0; i < path.NodeIds.Count; i++)
        {
            var nodeId = path.NodeIds[i];
            var node = _query.GetNode(nodeId);

                if (node is not null && IsNodeGrounded(node))
                    groundedNodes++;

            if (i < path.EdgeKinds.Count)
            {
                var outgoing = _index.EdgeIdx.OutgoingByKind;
                if (outgoing.TryGetValue(nodeId, out var nodeEdges))
                {
                    var edge = nodeEdges.FirstOrDefault(e => e.Kind == path.EdgeKinds[i]);
                    if (!string.IsNullOrEmpty(edge.Kind))
                    {
                        var edgeScore = EdgeConfidenceCalculator.Calculate(edge);
                        totalConfidence *= edgeScore.Value;
                    }
                }
            }
        }

        var groundingRatio = path.NodeIds.Count > 0
            ? (double)groundedNodes / path.NodeIds.Count
            : 0;

        var lengthScore = path.NodeIds.Count <= 3 ? 1.0
            : path.NodeIds.Count <= 6 ? 0.7
            : 0.4;

        var score = totalConfidence * 0.4
                    + groundingRatio * 0.35
                    + lengthScore * 0.25;

        return new ScoredPath
        {
            Path = path,
            Score = Math.Round(score, 4),
            GroundedNodeCount = groundedNodes,
            TotalNodes = path.NodeIds.Count,
            AverageConfidence = Math.Round(totalConfidence, 4),
        };
    }

    private static bool IsNodeGrounded(GraphNode node)
    {
        return !string.IsNullOrEmpty(node.Attributes.GetValueOrDefault("symbolHandle", ""));
    }
}

public sealed class SemanticPathOptions
{
    public TraversalDirection Direction { get; init; } = TraversalDirection.Forward;
    public int MaxDepth { get; init; } = 6;
    public int MaxPaths { get; init; } = 20;
    public IReadOnlyList<string>? EdgeKinds { get; init; }
    public IReadOnlyList<string>? NodeKinds { get; init; }
    public ResolutionConfidence MinConfidence { get; init; } = ResolutionConfidence.Medium;
    public double MinPathScore { get; init; } = 0.3;

    public static SemanticPathOptions Default => new();
}

public sealed class ScoredPath
{
    public required SemanticPath Path { get; init; }
    public double Score { get; init; }
    public int GroundedNodeCount { get; init; }
    public int TotalNodes { get; init; }
    public double AverageConfidence { get; init; }
}

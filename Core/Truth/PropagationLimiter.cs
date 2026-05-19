// =============================================================================
// Truth/PropagationLimiter.cs — prevents low-confidence edges from polluting the graph
// =============================================================================
// Rules:
//   - Max propagation depth from a grounded source edge
//   - Edges below confidence threshold do not extend traversal
//   - Ungrounded edges are never traversed
//   - Score decays with distance from source facts
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;

namespace Core.Truth;

public sealed class PropagationLimiter
{
    private readonly PropagationLimiterOptions _options;

    public PropagationLimiter(PropagationLimiterOptions? options = null)
    {
        _options = options ?? PropagationLimiterOptions.Default;
    }

    public bool ShouldTraverse(EdgeInfo edge, int currentDepth, TruthScore? edgeScore = null)
    {
        if (currentDepth >= _options.MaxPropagationDepth)
            return false;

        var groundedStr = edge.GetAttr("grounded");
        if (groundedStr == "false")
            return false;

        var score = edgeScore ?? EdgeConfidenceCalculator.Calculate(edge);

        if (!score.CanPropagate || score.Value < _options.MinConfidenceThreshold)
            return false;

        if (score.PropagationDepth + currentDepth > _options.MaxPropagationDepth)
            return false;

        return true;
    }

    public bool ShouldIncludeInContext(TruthScore score)
    {
        if (!_options.AllowInferredContext && score.IsInferred)
            return false;

        return score.CanEnterContext;
    }

    public IReadOnlyList<EdgeInfo> FilterPropagable(
        IEnumerable<EdgeInfo> edges,
        int currentDepth)
    {
        var result = new List<EdgeInfo>();
        foreach (var edge in edges)
        {
            if (ShouldTraverse(edge, currentDepth))
                result.Add(edge);
        }
        return result;
    }

    public TruthScore DecayScore(TruthScore score, int stepsFromSource)
    {
        return score.Decay(stepsFromSource);
    }
}

public sealed class PropagationLimiterOptions
{
    public double MinConfidenceThreshold { get; init; } = 0.4;
    public int MaxPropagationDepth { get; init; } = 4;
    public bool AllowInferredContext { get; init; } = false;
    public bool AllowUngroundedContext { get; init; } = false;

    public static PropagationLimiterOptions Default => new();
}

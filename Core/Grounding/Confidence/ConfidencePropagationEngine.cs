// =============================================================================
// Grounding/Confidence/ConfidencePropagationEngine.cs — deterministic propagation
// =============================================================================
// Determinism: BFS with sorted neighbor expansion (StringComparer.Ordinal).
//   - Every edge type maps to a fixed decay factor.
//   - Propagation order is fully determined by start nodes, graph topology, and decay rules.
// Provenance: PropagationResult records the exact edge path taken for each node.
//   - PropagationTrace records per-node confidence lineage.
// Replay: PropagationResult implements IEquatable for regression comparison.
//   - PropagationSnapshot captures immutable propagation state.
// Grounding: speculative ancestry is transitive; once speculative, always speculative.
// =============================================================================

using System.Diagnostics.CodeAnalysis;
using Core.Graph.Indexing;
using Core.Truth;

namespace Core.Grounding.Confidence;

public sealed class ConfidencePropagationEngine
{
    private readonly GraphIndex _graphIndex;
    private readonly EdgeConfidencePolicy _policy;
    private readonly ConfidencePropagationOptions _options;

    public ConfidencePropagationEngine(
        GraphIndex graphIndex,
        EdgeConfidencePolicy? policy = null,
        ConfidencePropagationOptions? options = null)
    {
        _graphIndex = graphIndex ?? throw new ArgumentNullException(nameof(graphIndex));
        _policy = policy ?? EdgeConfidencePolicy.Default;
        _options = options ?? ConfidencePropagationOptions.Default;
    }

    public PropagationResult Propagate(
        IReadOnlyList<string> startNodeIds,
        PropagationDirection direction = PropagationDirection.Outgoing)
    {
        var confidenceMap = new Dictionary<string, NodeConfidenceEntry>(StringComparer.Ordinal);
        var trace = new List<PropagationStep>();
        var nodeOrder = new List<string>();
        var queue = new Queue<PropagationFrontier>();

        foreach (var nodeId in startNodeIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!_graphIndex.Nodes.ContainsKey(nodeId)) continue;

            var initial = new GroundingConfidence(_options.InitialConfidenceScore, 0);
            confidenceMap[nodeId] = new NodeConfidenceEntry(nodeId, initial, null, null);
            queue.Enqueue(new PropagationFrontier(nodeId, initial, null, null));
            nodeOrder.Add(nodeId);

            trace.Add(new PropagationStep
            {
                StepIndex = trace.Count,
                NodeId = nodeId,
                IncomingEdgeKind = null,
                IncomingEdgeFrom = null,
                IncomingConfidence = initial,
                PropagatedConfidence = initial,
                Action = PropagationAction.Initialized,
            });
        }

        while (queue.Count > 0)
        {
            var frontier = queue.Dequeue();

            var neighbors = direction switch
            {
                PropagationDirection.Outgoing => GetOutgoingNeighbors(frontier.NodeId),
                PropagationDirection.Incoming => GetIncomingNeighbors(frontier.NodeId),
                PropagationDirection.Both => GetOutgoingNeighbors(frontier.NodeId)
                    .Concat(GetIncomingNeighbors(frontier.NodeId))
                    .ToList(),
                _ => GetOutgoingNeighbors(frontier.NodeId),
            };

            foreach (var (neighborId, edgeKind) in neighbors
                .OrderBy(n => n.NodeId, StringComparer.Ordinal))
            {
                var propagated = _policy.Propagate(frontier.Confidence, edgeKind);

                if (propagated.Level <= _options.MinConfidenceLevel
                    && _options.StopAtMinConfidence)
                    continue;

                if (propagated.HopDistance > _options.MaxPropagationDepth)
                    continue;

                var shouldUpdate = !confidenceMap.TryGetValue(neighborId, out var existing)
                    || propagated.Score > existing.PropagatedConfidence.Score
                    || (Math.Abs(propagated.Score - existing.PropagatedConfidence.Score) < _options.ScoreEqualityEpsilon
                        && propagated.HopDistance < existing.PropagatedConfidence.HopDistance);

                if (!shouldUpdate) continue;

                var entry = new NodeConfidenceEntry(
                    neighborId, propagated, frontier.NodeId, edgeKind);
                confidenceMap[neighborId] = entry;

                if (!nodeOrder.Contains(neighborId, StringComparer.Ordinal))
                    nodeOrder.Add(neighborId);

                queue.Enqueue(new PropagationFrontier(neighborId, propagated, frontier.NodeId, edgeKind));

                trace.Add(new PropagationStep
                {
                    StepIndex = trace.Count,
                    NodeId = neighborId,
                    IncomingEdgeKind = edgeKind,
                    IncomingEdgeFrom = frontier.NodeId,
                    IncomingConfidence = frontier.Confidence,
                    PropagatedConfidence = propagated,
                    Action = existing is null
                        ? PropagationAction.Propagated
                        : PropagationAction.Updated,
                });
            }
        }

        var entries = nodeOrder
            .Select(id => confidenceMap[id])
            .ToList();

        return new PropagationResult
        {
            Entries = entries,
            Trace = trace,
            StartNodeIds = startNodeIds,
            Direction = direction,
            TotalNodesReached = entries.Count,
        };
    }

    private List<(string NodeId, string EdgeKind)> GetOutgoingNeighbors(string nodeId)
    {
        var result = new List<(string, string)>();
        if (_graphIndex.EdgeIdx.OutgoingByKind.TryGetValue(nodeId, out var outEdges))
        {
            foreach (var edge in outEdges
                .Where(e => ShouldTraverseEdge(e))
                .OrderBy(e => e.ToId, StringComparer.Ordinal))
            {
                result.Add((edge.ToId, MapEdgeKind(edge.Kind, edge.Evidence)));
            }
        }
        return result;
    }

    private List<(string NodeId, string EdgeKind)> GetIncomingNeighbors(string nodeId)
    {
        var result = new List<(string, string)>();
        if (_graphIndex.EdgeIdx.IncomingByKind.TryGetValue(nodeId, out var inEdges))
        {
            foreach (var edge in inEdges
                .Where(e => ShouldTraverseEdge(e))
                .OrderBy(e => e.ToId, StringComparer.Ordinal))
            {
                result.Add((edge.ToId, MapEdgeKind(edge.Kind, edge.Evidence)));
            }
        }
        return result;
    }

    private bool ShouldTraverseEdge(EdgeInfo edge)
    {
        if (!edge.Grounded && _options.SkipUngroundedEdges) return false;

        var confidence = edge.Confidence;
        if (confidence == Core.Graph.EdgeConfidenceKinds.Low && _options.SkipLowConfidenceEdges)
            return false;

        return true;
    }

    private static string MapEdgeKind(string graphEdgeKind, string evidence)
    {
        if (graphEdgeKind == Core.Graph.GraphEdgeKinds.Call)
        {
            return evidence switch
            {
                Core.Graph.EdgeEvidenceKinds.SemanticDirect => EdgeDecayKind.ExplicitInvocation,
                Core.Graph.EdgeEvidenceKinds.SemanticInferred => EdgeDecayKind.SemanticSimilarity,
                Core.Graph.EdgeEvidenceKinds.SyntaxDirect => EdgeDecayKind.ControlFlow,
                Core.Graph.EdgeEvidenceKinds.SyntaxPattern => EdgeDecayKind.SemanticSimilarity,
                _ => EdgeDecayKind.PropagationInference,
            };
        }

        return graphEdgeKind switch
        {
            "nh:entity-access" => EdgeDecayKind.DataFlow,
            "spring:implements" => EdgeDecayKind.ConfigurationBinding,
            "spring:bean" => EdgeDecayKind.ConfigurationBinding,
            _ => EdgeDecayKind.PropagationInference,
        };
    }

    public PropagationSnapshot CaptureSnapshot(PropagationResult result)
    {
        return PropagationSnapshot.From(result);
    }
}

public sealed class ConfidencePropagationOptions
{
    public double InitialConfidenceScore { get; init; } = 1.0;
    public int MaxPropagationDepth { get; init; } = 5;
    public double ScoreEqualityEpsilon { get; init; } = 0.0001;
    public ConfidenceLevel MinConfidenceLevel { get; init; } = ConfidenceLevel.Unsupported;
    public bool StopAtMinConfidence { get; init; } = true;
    public bool SkipUngroundedEdges { get; init; } = true;
    public bool SkipLowConfidenceEdges { get; init; } = true;

    public static ConfidencePropagationOptions Default => new();
}

public enum PropagationDirection
{
    Outgoing = 0,
    Incoming = 1,
    Both = 2,
}

public enum PropagationAction
{
    Initialized = 0,
    Propagated = 1,
    Updated = 2,
}

public sealed class NodeConfidenceEntry : IEquatable<NodeConfidenceEntry>
{
    public required string NodeId { get; init; }
    public required GroundingConfidence PropagatedConfidence { get; init; }
    public string? ViaNodeId { get; init; }
    public string? ViaEdgeKind { get; init; }

    [SetsRequiredMembers]
    public NodeConfidenceEntry(
        string nodeId,
        GroundingConfidence confidence,
        string? viaNodeId,
        string? viaEdgeKind)
    {
        NodeId = nodeId;
        PropagatedConfidence = confidence;
        ViaNodeId = viaNodeId;
        ViaEdgeKind = viaEdgeKind;
    }

    public bool Equals(NodeConfidenceEntry? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(NodeId, other.NodeId)
            && PropagatedConfidence == other.PropagatedConfidence
            && StringComparer.Ordinal.Equals(ViaNodeId ?? "", other.ViaNodeId ?? "");
    }

    public override bool Equals(object? obj) => obj is NodeConfidenceEntry other && Equals(other);
    public override int GetHashCode() => NodeId.GetHashCode(StringComparison.Ordinal);
}

public sealed class PropagationStep
{
    public int StepIndex { get; init; }
    public required string NodeId { get; init; }
    public string? IncomingEdgeKind { get; init; }
    public string? IncomingEdgeFrom { get; init; }
    public required GroundingConfidence IncomingConfidence { get; init; }
    public required GroundingConfidence PropagatedConfidence { get; init; }
    public PropagationAction Action { get; init; }

    public override string ToString() =>
        $"[{StepIndex:D5}] {Action} {NodeId} confidence={PropagatedConfidence.Score:F3} via={IncomingEdgeKind ?? "init"}";
}

public sealed class PropagationResult : IEquatable<PropagationResult>
{
    public required IReadOnlyList<NodeConfidenceEntry> Entries { get; init; }
    public required IReadOnlyList<PropagationStep> Trace { get; init; }
    public required IReadOnlyList<string> StartNodeIds { get; init; }
    public PropagationDirection Direction { get; init; }
    public int TotalNodesReached { get; init; }

    public GroundingConfidence? GetConfidence(string nodeId)
    {
        foreach (var e in Entries)
            if (StringComparer.Ordinal.Equals(e.NodeId, nodeId))
                return e.PropagatedConfidence;
        return null;
    }

    public bool Equals(PropagationResult? other)
    {
        if (other is null) return false;
        if (TotalNodesReached != other.TotalNodesReached) return false;
        if (Direction != other.Direction) return false;
        if (Entries.Count != other.Entries.Count) return false;
        if (StartNodeIds.Count != other.StartNodeIds.Count) return false;

        for (var i = 0; i < StartNodeIds.Count; i++)
            if (!StringComparer.Ordinal.Equals(StartNodeIds[i], other.StartNodeIds[i]))
                return false;

        for (var i = 0; i < Entries.Count; i++)
            if (!Entries[i].Equals(other.Entries[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is PropagationResult other && Equals(other);
    public override int GetHashCode() => TotalNodesReached;
}

public sealed class PropagationSnapshot
{
    public int TotalNodesReached { get; init; }
    public IReadOnlyList<string> NodeIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> ConfidenceScores { get; init; } = Array.Empty<double>();
    public IReadOnlyList<ConfidenceLevel> ConfidenceLevels { get; init; } = Array.Empty<ConfidenceLevel>();
    public int SpeculativeCount { get; init; }
    public int UnsupportedCount { get; init; }
    public string CapturedAt { get; init; } = "";

    public static PropagationSnapshot From(PropagationResult result)
    {
        return new PropagationSnapshot
        {
            TotalNodesReached = result.TotalNodesReached,
            NodeIds = result.Entries.Select(e => e.NodeId).ToList(),
            ConfidenceScores = result.Entries.Select(e => e.PropagatedConfidence.Score).ToList(),
            ConfidenceLevels = result.Entries.Select(e => e.PropagatedConfidence.Level).ToList(),
            SpeculativeCount = result.Entries.Count(e =>
                e.PropagatedConfidence.Level >= ConfidenceLevel.Speculative
                && e.PropagatedConfidence.Level < ConfidenceLevel.Unsupported),
            UnsupportedCount = result.Entries.Count(e =>
                e.PropagatedConfidence.Level == ConfidenceLevel.Unsupported),
            CapturedAt = System.DateTime.UtcNow.ToString("O"),
        };
    }

    public bool Equals(PropagationSnapshot? other)
    {
        if (other is null) return false;
        if (TotalNodesReached != other.TotalNodesReached) return false;
        if (SpeculativeCount != other.SpeculativeCount) return false;
        if (UnsupportedCount != other.UnsupportedCount) return false;
        if (NodeIds.Count != other.NodeIds.Count) return false;

        for (var i = 0; i < NodeIds.Count; i++)
        {
            if (!StringComparer.Ordinal.Equals(NodeIds[i], other.NodeIds[i])) return false;
            if (Math.Abs(ConfidenceScores[i] - other.ConfidenceScores[i]) > 0.0001) return false;
            if (ConfidenceLevels[i] != other.ConfidenceLevels[i]) return false;
        }

        return true;
    }
}

internal sealed class PropagationFrontier
{
    public string NodeId { get; }
    public GroundingConfidence Confidence { get; }
    public string? ParentNodeId { get; }
    public string? EdgeKind { get; }

    public PropagationFrontier(
        string nodeId,
        GroundingConfidence confidence,
        string? parentNodeId,
        string? edgeKind)
    {
        NodeId = nodeId;
        Confidence = confidence;
        ParentNodeId = parentNodeId;
        EdgeKind = edgeKind;
    }
}

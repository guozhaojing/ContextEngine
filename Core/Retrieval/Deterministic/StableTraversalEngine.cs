// =============================================================================
// Retrieval/Deterministic/StableTraversalEngine.cs — deterministic BFS traversal
// =============================================================================
// Enforces: same graph + same start nodes + same options → same traversal output.
//   - Sorted neighbor expansion (by nodeId then edgeKind then ToId)
//   - Stable BFS queue (no HashSet-based ordering)
//   - Determinisic path deduplication
//   - Truth-aware propagation filtering with PropagationLimiter
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Truth;

namespace Core.Retrieval.Deterministic;

public static class StableTraversalEngine
{
    public static IReadOnlyList<SemanticPath> Traverse(
        GraphIndex index,
        IReadOnlyList<string> startIds,
        SemanticTraversalOptions options)
    {
        var paths = new List<SemanticPath>();
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);
        var limiter = new PropagationLimiter();

        foreach (var startId in startIds)
        {
            if (paths.Count >= options.MaxPaths)
                break;

            BfsExplore(
                index, startId, options, limiter,
                new List<string> { startId },
                new List<string>(),
                new List<string>(),
                new HashSet<string>(StringComparer.Ordinal) { startId },
                paths,
                seenSignatures);
        }

        return paths
            .OrderBy(p => p.NodeIds.Count)
            .ThenBy(p => p.PathId, StringComparer.Ordinal)
            .ToList();
    }

    private static void BfsExplore(
        GraphIndex index,
        string currentId,
        SemanticTraversalOptions options,
        PropagationLimiter limiter,
        List<string> nodePath,
        List<string> edgeKinds,
        List<string> labels,
        HashSet<string> visiting,
        List<SemanticPath> paths,
        HashSet<string> seenSignatures)
    {
        if (paths.Count >= options.MaxPaths)
            return;

        if (CheckTarget(currentId, options, index))
        {
            AddPath(nodePath, edgeKinds, labels, paths, seenSignatures);
            return;
        }

        var atMaxDepth = options.MaxDepth.HasValue
            && nodePath.Count - 1 >= options.MaxDepth.Value;

        var edges = GetAdjacentEdgesSorted(index, currentId, options);
        if (edges.Count == 0 || atMaxDepth)
        {
            AddPath(nodePath, edgeKinds, labels, paths, seenSignatures);
            return;
        }

        var validEdges = new List<(EdgeInfo Edge, int Index)>();
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (visiting.Contains(edge.ToId)) continue;

            if (options.NodeKinds is not null
                && index.Nodes.TryGetValue(edge.ToId, out var targetNode)
                && !options.NodeKinds.Contains(targetNode.Kind))
                continue;

            if (!limiter.ShouldTraverse(edge, nodePath.Count))
                continue;

            validEdges.Add((edge, i));
        }

        if (validEdges.Count == 0)
        {
            AddPath(nodePath, edgeKinds, labels, paths, seenSignatures);
            return;
        }

        foreach (var (edge, _) in validEdges)
        {
            nodePath.Add(edge.ToId);
            edgeKinds.Add(edge.Kind);
            labels.Add(edge.Label);
            visiting.Add(edge.ToId);

            BfsExplore(index, edge.ToId, options, limiter,
                nodePath, edgeKinds, labels, visiting, paths, seenSignatures);

            visiting.Remove(edge.ToId);
            labels.RemoveAt(labels.Count - 1);
            edgeKinds.RemoveAt(edgeKinds.Count - 1);
            nodePath.RemoveAt(nodePath.Count - 1);
        }
    }

    private static void AddPath(
        List<string> nodePath,
        List<string> edgeKinds,
        List<string> labels,
        List<SemanticPath> paths,
        HashSet<string> seenSignatures)
    {
        var signature = string.Join("|", nodePath);
        if (!seenSignatures.Add(signature))
            return;

        paths.Add(new SemanticPath
        {
            PathId = signature,
            NodeIds = nodePath.ToList(),
            EdgeKinds = edgeKinds.ToList(),
            HopLabels = labels.ToList(),
            Summary = BuildSummary(nodePath, edgeKinds)
        });
    }

    private static bool CheckTarget(string currentId, SemanticTraversalOptions options, GraphIndex index)
    {
        if (options.TargetAttributeKey is null)
            return false;

        if (!index.Nodes.TryGetValue(currentId, out var node))
            return false;

        if (!node.Attributes.TryGetValue(options.TargetAttributeKey, out var value))
            return false;

        if (options.TargetAttributeValue is not null
            && !string.Equals(value, options.TargetAttributeValue, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static IReadOnlyList<EdgeInfo> GetAdjacentEdgesSorted(
        GraphIndex index,
        string nodeId,
        SemanticTraversalOptions options)
    {
        var dict = options.Direction == TraversalDirection.Backward
            ? index.EdgeIdx.IncomingByKind
            : index.EdgeIdx.OutgoingByKind;

        if (!dict.TryGetValue(nodeId, out var allEdges))
            return Array.Empty<EdgeInfo>();

        var filtered = options.EdgeKinds is not null
            ? allEdges.Where(e => options.EdgeKinds.Contains(e.Kind))
            : allEdges;

        return filtered
            .OrderBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.ToId, StringComparer.Ordinal)
            .ThenBy(e => e.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildSummary(List<string> nodeIds, List<string> edgeKinds)
    {
        var parts = new List<string>();
        for (var i = 0; i < nodeIds.Count; i++)
        {
            var id = nodeIds[i];
            var shortName = id.Contains("::") ? id[(id.LastIndexOf("::") + 2)..] : id;
            if (shortName.Length > 40)
                shortName = shortName[..37] + "...";
            parts.Add(shortName);

            if (i < edgeKinds.Count)
                parts.Add($"─[{edgeKinds[i]}]→");
        }

        return string.Join(" ", parts);
    }
}

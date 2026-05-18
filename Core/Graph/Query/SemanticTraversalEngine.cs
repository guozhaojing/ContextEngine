using Core.Graph.Analysis;
using Core.Graph.Indexing;

namespace Core.Graph.Query;

/// <summary>
/// Layer-aware BFS 遍历引擎 — 支持 Kind/Node/Confidence 多维过滤。
/// </summary>
internal static class SemanticTraversalEngine
{
    /// <summary>
    /// BFS 遍历 — 从一组起点出发，沿指定方向的边向前/向后搜索。
    /// </summary>
    public static IReadOnlyList<SemanticPath> Traverse(
        GraphIndex index,
        IEnumerable<string> startIds,
        SemanticTraversalOptions options)
    {
        var paths = new List<SemanticPath>();
        var seenPathSignatures = options.DeduplicatePaths
            ? new HashSet<string>(StringComparer.Ordinal)
            : null;

        foreach (var startId in startIds)
        {
            if (paths.Count >= options.MaxPaths)
                break;

            BfsExplore(
                index, startId, options,
                new List<string> { startId },
                new List<string>(),
                new List<string>(),
                new HashSet<string>(StringComparer.Ordinal) { startId },
                paths,
                seenPathSignatures);
        }

        return paths;
    }

    private static void BfsExplore(
        GraphIndex index,
        string currentId,
        SemanticTraversalOptions options,
        List<string> nodePath,
        List<string> edgeKinds,
        List<string> labels,
        HashSet<string> visiting,
        List<SemanticPath> paths,
        HashSet<string>? seenSignatures)
    {
        if (paths.Count >= options.MaxPaths)
            return;

        // 检查终止条件 (在扩展前)
        if (CheckTarget(currentId, options, index))
        {
            AddPath(nodePath, edgeKinds, labels, paths, seenSignatures);
            return;
        }

        // Depth 限制 — 到达最大深度后，记录路径但不继续扩展
        var atMaxDepth = options.MaxDepth.HasValue
            && nodePath.Count - 1 >= options.MaxDepth.Value;

        // 取邻接边
        var edges = GetAdjacentEdges(index, currentId, options);
        if (edges.Count == 0 || atMaxDepth)
        {
            AddPath(nodePath, edgeKinds, labels, paths, seenSignatures);
            return;
        }

        var hasValidNext = false;
        foreach (var edge in edges)
        {
            // 环检测
            if (visiting.Contains(edge.ToId))
                continue;

            // NodeKind 过滤
            if (options.NodeKinds is not null
                && index.Nodes.TryGetValue(edge.ToId, out var targetNode)
                && !options.NodeKinds.Contains(targetNode.Kind))
                continue;

            // Confidence 过滤
            if (options.MinConfidence.HasValue)
            {
                var edgeConf = ParseConfidence(edge.GetAttr("confidence"));
                if (edgeConf.HasValue && edgeConf.Value < options.MinConfidence.Value)
                    continue;
            }

            hasValidNext = true;

            nodePath.Add(edge.ToId);
            edgeKinds.Add(edge.Kind);
            labels.Add(edge.Label);
            visiting.Add(edge.ToId);

            BfsExplore(index, edge.ToId, options, nodePath, edgeKinds, labels,
                visiting, paths, seenSignatures);

            visiting.Remove(edge.ToId);
            labels.RemoveAt(labels.Count - 1);
            edgeKinds.RemoveAt(edgeKinds.Count - 1);
            nodePath.RemoveAt(nodePath.Count - 1);
        }

        if (!hasValidNext)
            AddPath(nodePath, edgeKinds, labels, paths, seenSignatures);
    }

    private static void AddPath(
        List<string> nodePath,
        List<string> edgeKinds,
        List<string> labels,
        List<SemanticPath> paths,
        HashSet<string>? seenSignatures)
    {
        var signature = string.Join("|", nodePath);
        if (seenSignatures is not null && !seenSignatures.Add(signature))
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

    private static IReadOnlyList<EdgeInfo> GetAdjacentEdges(
        GraphIndex index,
        string nodeId,
        SemanticTraversalOptions options)
    {
        var dict = options.Direction == TraversalDirection.Backward
            ? index.EdgeIdx.IncomingByKind
            : index.EdgeIdx.OutgoingByKind;

        if (!dict.TryGetValue(nodeId, out var allEdges))
            return Array.Empty<EdgeInfo>();

        if (options.EdgeKinds is null)
            return allEdges;

        return allEdges.Where(e => options.EdgeKinds.Contains(e.Kind)).ToList();
    }

    private static ResolutionConfidence? ParseConfidence(string confidenceStr)
    {
        if (string.IsNullOrEmpty(confidenceStr))
            return null;

        return confidenceStr.ToLowerInvariant() switch
        {
            "exact" => ResolutionConfidence.Exact,
            "high" => ResolutionConfidence.High,
            "medium" => ResolutionConfidence.Medium,
            "low" => ResolutionConfidence.Low,
            _ => null
        };
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

using Core.Graph;

namespace Core.Retrieval.Ranking;

public sealed class DependencyDepthAnalyzer
{
    private readonly GraphQueryService _query;
    private readonly Dictionary<string, int> _entryPointDist = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _dataAccessDist = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _layerDepth = new(StringComparer.Ordinal);

    public DependencyDepthAnalyzer(GraphQueryService query)
    {
        _query = query;
        Precompute();
    }

    private void Precompute()
    {
        ComputeEntryPointDistances();
        ComputeDataAccessDistances();
        ComputeLayerDepths();
    }

    private void ComputeEntryPointDistances()
    {
        var entryPoints = _query.FindEntryPointNodes();
        if (entryPoints.Count == 0) return;

        // Multi-source BFS forward (follow callees)
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string id, int dist)>();

        foreach (var ep in entryPoints)
        {
            visited.Add(ep);
            queue.Enqueue((ep, 0));
            _entryPointDist[ep] = 0;
        }

        while (queue.Count > 0)
        {
            var (current, dist) = queue.Dequeue();
            if (dist >= 15) continue;

            foreach (var calleeId in _query.GetCallees(current))
            {
                if (visited.Add(calleeId))
                {
                    _entryPointDist[calleeId] = dist + 1;
                    queue.Enqueue((calleeId, dist + 1));
                }
            }
        }
    }

    private void ComputeDataAccessDistances()
    {
        // Find entity nodes — they are the "data" nodes
        var entityNodes = _query.GetAllNodes()
            .Where(n => n.Kind == GraphNodeKind.Entity)
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (entityNodes.Count == 0) return;

        // Multi-source BFS backward (follow callers) from entity nodes
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string id, int dist)>();

        foreach (var en in entityNodes)
        {
            visited.Add(en);
            queue.Enqueue((en, 0));
            _dataAccessDist[en] = 0;
        }

        while (queue.Count > 0)
        {
            var (current, dist) = queue.Dequeue();
            if (dist >= 10) continue;

            foreach (var callerId in _query.GetCallers(current))
            {
                if (visited.Add(callerId))
                {
                    _dataAccessDist[callerId] = dist + 1;
                    queue.Enqueue((callerId, dist + 1));
                }
            }
        }
    }

    private void ComputeLayerDepths()
    {
        var layerOrder = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["route"] = 0,
            ["controller"] = 1,
            ["service"] = 2,
            ["repository"] = 3,
            ["entity"] = 4,
            ["table"] = 5
        };

        foreach (var node in _query.GetAllNodes())
        {
            var layer = InferNodeLayer(node);
            _layerDepth[node.Id] = layerOrder.GetValueOrDefault(layer, 3);
        }
    }

    private static string InferNodeLayer(GraphNode node)
    {
        if (node.Attributes.ContainsKey("aspnet-route:entry-point") || node.Attributes.ContainsKey("route"))
            return "route";
        if (node.Kind == "entity") return "entity";
        if (node.Kind == "table") return "table";
        if ((node.ClassName ?? "").EndsWith("Controller", StringComparison.Ordinal)) return "controller";
        if ((node.ClassName ?? "").EndsWith("Service", StringComparison.Ordinal)) return "service";
        if ((node.ClassName ?? "").EndsWith("Repository", StringComparison.Ordinal)) return "repository";
        return "service";
    }

    public int GetEntryPointDistance(string nodeId) =>
        _entryPointDist.TryGetValue(nodeId, out var d) ? d : -1;

    public int GetDataAccessDistance(string nodeId) =>
        _dataAccessDist.TryGetValue(nodeId, out var d) ? d : -1;

    public int GetLayerDepth(string nodeId) =>
        _layerDepth.GetValueOrDefault(nodeId, 3);

    public int GetDependencyDepth(IEnumerable<string> nodeIds)
    {
        var minEntryDist = int.MaxValue;
        var minDataDist = int.MaxValue;

        foreach (var nid in nodeIds)
        {
            var ed = GetEntryPointDistance(nid);
            if (ed >= 0) minEntryDist = Math.Min(minEntryDist, ed);

            var dd = GetDataAccessDistance(nid);
            if (dd >= 0) minDataDist = Math.Min(minDataDist, dd);
        }

        var entryD = minEntryDist == int.MaxValue ? -1 : minEntryDist;
        var dataD = minDataDist == int.MaxValue ? -1 : minDataDist;

        if (entryD >= 0 && dataD >= 0) return entryD + dataD;
        if (entryD >= 0) return entryD;
        if (dataD >= 0) return dataD;
        return -1;
    }

    public int GetMaxLayerDepth(IEnumerable<string> nodeIds)
    {
        var maxDepth = -1;
        foreach (var nid in nodeIds)
        {
            var d = GetLayerDepth(nid);
            if (d > maxDepth) maxDepth = d;
        }
        return maxDepth;
    }
}

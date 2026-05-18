using Core.Graph;

namespace Core.Retrieval.Ranking;

public sealed class BusinessSignalAnalyzer
{
    private readonly GraphQueryService _query;
    private readonly HashSet<string> _entryPointIds;
    private readonly HashSet<string> _entityNodeIds;
    private readonly Dictionary<string, List<string>> _nodeEntities;
    private readonly Dictionary<string, List<string>> _nodeTables;
    private readonly Dictionary<string, List<string>> _nodeRoutes;
    private readonly HashSet<string> _entityAccessIds;

    public BusinessSignalAnalyzer(GraphQueryService query)
    {
        _query = query;
        _entryPointIds = new HashSet<string>(_query.FindEntryPointNodes(), StringComparer.Ordinal);
        _entityNodeIds = new HashSet<string>(StringComparer.Ordinal);
        _nodeEntities = new(StringComparer.Ordinal);
        _nodeTables = new(StringComparer.Ordinal);
        _nodeRoutes = new(StringComparer.Ordinal);
        _entityAccessIds = new HashSet<string>(StringComparer.Ordinal);

        Precompute();
    }

    private void Precompute()
    {
        foreach (var node in _query.GetAllNodes())
        {
            if (node.Kind == GraphNodeKind.Entity)
                _entityNodeIds.Add(node.Id);

            // Collect route patterns
            if (node.Attributes.TryGetValue("route", out var route) && !string.IsNullOrEmpty(route))
            {
                if (!_nodeRoutes.ContainsKey(node.Id))
                    _nodeRoutes[node.Id] = new List<string>();
                _nodeRoutes[node.Id].Add(route);
            }

            // Collect entity data
            if (node.Attributes.TryGetValue("nh:entity-class", out var ec) && !string.IsNullOrEmpty(ec))
            {
                if (!_nodeEntities.ContainsKey(node.Id))
                    _nodeEntities[node.Id] = new List<string>();
                _nodeEntities[node.Id].Add(ec);
                _entityAccessIds.Add(node.Id);
            }

            if (node.Attributes.TryGetValue("nh:table", out var tb) && !string.IsNullOrEmpty(tb))
            {
                if (!_nodeTables.ContainsKey(node.Id))
                    _nodeTables[node.Id] = new List<string>();
                _nodeTables[node.Id].Add(tb);
                _entityAccessIds.Add(node.Id);
            }

            // Check entity access via edge attributes
            if (node.Attributes.ContainsKey("nh:entity-access"))
                _entityAccessIds.Add(node.Id);
        }
    }

    public bool IsEntryPoint(string nodeId) =>
        _entryPointIds.Contains(nodeId);

    public bool IsEntityAccess(string nodeId) =>
        _entityAccessIds.Contains(nodeId);

    public int EntryPointCount => _entryPointIds.Count;

    public IReadOnlyList<string> GetRelatedEntities(IEnumerable<string> nodeIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nid in nodeIds)
        {
            if (_nodeEntities.TryGetValue(nid, out var entities))
                foreach (var e in entities) result.Add(e);
        }
        return result.ToList();
    }

    public IReadOnlyList<string> GetRelatedTables(IEnumerable<string> nodeIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nid in nodeIds)
        {
            if (_nodeTables.TryGetValue(nid, out var tables))
                foreach (var t in tables) result.Add(t);
        }
        return result.ToList();
    }

    public IReadOnlyList<string> GetRelatedRoutes(IEnumerable<string> nodeIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nid in nodeIds)
        {
            if (_nodeRoutes.TryGetValue(nid, out var routes))
                foreach (var r in routes) result.Add(r);
        }
        return result.ToList();
    }

    public double GetBusinessScore(
        string nodeId,
        int entryPointDistance,
        int dataAccessDistance)
    {
        var score = 0.0;

        // Proximity to entry point (closer = more important)
        if (entryPointDistance >= 0 && entryPointDistance <= 5)
            score += Math.Max(0, 2.0 - entryPointDistance * 0.4);

        // Proximity to data access (closer = more important)
        if (dataAccessDistance >= 0 && dataAccessDistance <= 3)
            score += Math.Max(0, 2.0 - dataAccessDistance * 0.6);

        // Entry point itself
        if (IsEntryPoint(nodeId)) score += 2.0;

        // Entity access itself
        if (IsEntityAccess(nodeId)) score += 1.5;

        // Normalize to 0-1
        return Math.Min(score / 5.0, 1.0);
    }
}

namespace Core.Graph;

public class GraphQueryService
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly Dictionary<string, List<string>> _callers;
    private readonly Dictionary<string, List<string>> _callees;

    public GraphQueryService(CodeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        _callers = graph.Nodes.ToDictionary(
            node => node.Id,
            node => node.CalledBy.ToList(),
            StringComparer.Ordinal);
        _callees = BuildCalleesIndex(graph.Edges);
    }

    public bool Contains(string methodId) => _nodes.ContainsKey(methodId);

    public GraphNode? GetNode(string methodId) =>
        _nodes.TryGetValue(methodId, out var node) ? node : null;

    public IReadOnlyList<string> GetCallers(string methodId)
    {
        EnsureExists(methodId);
        return _callers[methodId];
    }

    public IReadOnlyList<string> GetCallees(string methodId)
    {
        EnsureExists(methodId);
        return _callees.TryGetValue(methodId, out var callees) ? callees : [];
    }

    public IReadOnlyList<IReadOnlyList<string>> GetCallChain(string methodId, int depth)
    {
        EnsureExists(methodId);
        if (depth < 0)
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be >= 0.");

        if (depth == 0)
            return [[methodId]];

        var chains = new List<IReadOnlyList<string>>();
        var path = new List<string> { methodId };
        DfsCallChain(methodId, depth, path, chains);
        return chains;
    }

    public IReadOnlyList<string> FindEntryPoints(string methodId)
    {
        EnsureExists(methodId);

        var entryPoints = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        void WalkBack(string current)
        {
            if (!visiting.Add(current))
                return;

            var callers = GetCallers(current);
            if (callers.Count == 0)
            {
                entryPoints.Add(current);
                visiting.Remove(current);
                return;
            }

            foreach (var caller in callers)
                WalkBack(caller);

            visiting.Remove(current);
        }

        WalkBack(methodId);
        return entryPoints.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private void DfsCallChain(
        string current,
        int remainingDepth,
        List<string> path,
        List<IReadOnlyList<string>> chains)
    {
        if (remainingDepth == 0)
        {
            chains.Add(path.ToList());
            return;
        }

        var callees = GetCallees(current);
        if (callees.Count == 0)
        {
            chains.Add(path.ToList());
            return;
        }

        var extended = false;
        foreach (var callee in callees)
        {
            if (path.Contains(callee, StringComparer.Ordinal))
                continue;

            path.Add(callee);
            extended = true;
            DfsCallChain(callee, remainingDepth - 1, path, chains);
            path.RemoveAt(path.Count - 1);
        }

        if (!extended)
            chains.Add(path.ToList());
    }

    private static Dictionary<string, List<string>> BuildCalleesIndex(IEnumerable<GraphEdge> edges)
    {
        var callees = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            if (!callees.TryGetValue(edge.FromId, out var targets))
            {
                targets = [];
                callees[edge.FromId] = targets;
            }

            if (!targets.Contains(edge.ToId, StringComparer.Ordinal))
                targets.Add(edge.ToId);
        }

        return callees;
    }

    private void EnsureExists(string methodId)
    {
        if (!Contains(methodId))
            throw new KeyNotFoundException($"Method node not found: {methodId}");
    }
}

using Core.Graph;
using Core.Graph.Query;

namespace Core.Retrieval.Ranking;

public sealed class CentralityAnalyzer
{
    private readonly GraphQueryService _query;
    private readonly Dictionary<string, int> _callerCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _calleeCounts = new(StringComparer.Ordinal);
    private int _maxFan;

    public CentralityAnalyzer(GraphQueryService query)
    {
        _query = query;
        Precompute();
    }

    private void Precompute()
    {
        var nodes = _query.GetAllNodes().ToList();
        foreach (var node in nodes)
        {
            _callerCounts[node.Id] = 0;
            _calleeCounts[node.Id] = 0;
        }

        foreach (var node in nodes)
        {
            _callerCounts[node.Id] = _query.GetCallers(node.Id).Count;
            _calleeCounts[node.Id] = _query.GetCallees(node.Id).Count;
        }

        _maxFan = _callerCounts.Values.Concat(_calleeCounts.Values).DefaultIfEmpty(1).Max();
    }

    public int GetCallerCount(string nodeId) =>
        _callerCounts.GetValueOrDefault(nodeId);

    public int GetCalleeCount(string nodeId) =>
        _calleeCounts.GetValueOrDefault(nodeId);

    public int GetFanIn(string nodeId)
    {
        if (!_callerCounts.ContainsKey(nodeId)) return 0;

        var visited = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var queue = new Queue<string>();
        foreach (var caller in _query.GetCallers(nodeId))
        {
            if (visited.Add(caller)) queue.Enqueue(caller);
        }

        while (queue.Count > 0 && visited.Count < 200)
        {
            var current = queue.Dequeue();
            foreach (var c in _query.GetCallers(current))
            {
                if (visited.Add(c))
                {
                    queue.Enqueue(c);
                    if (visited.Count >= 200) break;
                }
            }
        }

        return visited.Count - 1; // exclude self
    }

    public int GetFanOut(string nodeId)
    {
        if (!_calleeCounts.ContainsKey(nodeId)) return 0;

        var visited = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var queue = new Queue<string>();
        foreach (var callee in _query.GetCallees(nodeId))
        {
            if (visited.Add(callee)) queue.Enqueue(callee);
        }

        while (queue.Count > 0 && visited.Count < 200)
        {
            var current = queue.Dequeue();
            foreach (var c in _query.GetCallees(current))
            {
                if (visited.Add(c))
                {
                    queue.Enqueue(c);
                    if (visited.Count >= 200) break;
                }
            }
        }

        return visited.Count - 1;
    }

    public double GetCentralityScore(string nodeId)
    {
        if (_maxFan == 0) return 0;

        var callerCount = GetCallerCount(nodeId);
        var calleeCount = GetCalleeCount(nodeId);
        var score = (double)(callerCount + calleeCount) / (_maxFan * 3);
        return Math.Min(score, 1.0);
    }

    public double GetPathFrequency(
        IEnumerable<string> nodeIds,
        IEnumerable<SemanticPath> allPaths)
    {
        var nodes = new HashSet<string>(nodeIds, StringComparer.Ordinal);
        if (nodes.Count == 0) return 0;

        var pathCount = 0;
        var hitCount = 0;

        foreach (var path in allPaths)
        {
            pathCount++;
            foreach (var nid in path.NodeIds)
            {
                if (nodes.Contains(nid))
                {
                    hitCount++;
                    break;
                }
            }
        }

        if (pathCount == 0) return 0;
        return Math.Min((double)hitCount / pathCount, 1.0);
    }
}

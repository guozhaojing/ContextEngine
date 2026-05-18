// =============================================================================
// Graph/GraphQueryService.cs — 代码图查询服务（只读）
// =============================================================================
// 【边界】只依赖 GraphIndex；不修改图；不调用 Builder / Semantic。
// API：GetCallers / GetCallees / GetCallChain / FindEntryPoints
// =============================================================================

using Core.Graph.Indexing;
using Core.Graph.Traversal;

namespace Core.Graph;

public sealed class GraphQueryService
{
    private readonly GraphIndex _index;

    public GraphQueryService(GraphIndex index)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public GraphQueryService(CodeGraphBuildResult buildResult)
        : this(buildResult.Index)
    {
    }

    public bool Contains(string methodId) => _index.Nodes.ContainsKey(methodId);

    public GraphNode? GetNode(string methodId) =>
        _index.Nodes.TryGetValue(methodId, out var node) ? node : null;

    /// <summary>谁调用了该方法？（上游 B ← A 中的 A）</summary>
    public IReadOnlyList<string> GetCallers(string methodId)
    {
        EnsureExists(methodId);
        return _index.Callers[methodId];
    }

    /// <summary>该方法调用了谁？（下游 A → B 中的 B）</summary>
    public IReadOnlyList<string> GetCallees(string methodId)
    {
        EnsureExists(methodId);
        return _index.Callees[methodId];
    }

    /// <summary>
    /// 从 methodId 向下展开调用链，depth 为向下走的边数。
    /// 返回多条路径（分支会产生多条链）。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> GetCallChain(string methodId, int depth)
    {
        EnsureExists(methodId);
        if (depth < 0)
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be >= 0.");

        if (depth == 0)
            return [[methodId]];

        var chains = new List<IReadOnlyList<string>>();
        var path = new List<string> { methodId };
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        DfsCallChain(methodId, depth, path, chains, visiting);
        return chains;
    }

    /// <summary>
    /// 沿 CalledBy 向上追溯到没有上游的入口方法（用于影响面/入口分析）。
    /// </summary>
    public IReadOnlyList<string> FindEntryPoints(string methodId)
    {
        EnsureExists(methodId);

        var entryPoints = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        WalkBack(methodId, entryPoints, visiting);
        return entryPoints.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private void WalkBack(string current, HashSet<string> entryPoints, HashSet<string> visiting)
    {
        if (!GraphTraversal.TryEnter(visiting, current))
            return;

        var callers = GetCallers(current);
        if (callers.Count == 0)
        {
            entryPoints.Add(current);
            GraphTraversal.Leave(visiting, current);
            return;
        }

        foreach (var caller in callers)
            WalkBack(caller, entryPoints, visiting);

        GraphTraversal.Leave(visiting, current);
    }

    private void DfsCallChain(
        string current,
        int remainingDepth,
        List<string> path,
        List<IReadOnlyList<string>> chains,
        HashSet<string> visiting)
    {
        if (!GraphTraversal.TryEnter(visiting, current))
        {
            chains.Add(path.ToList());
            return;
        }

        if (remainingDepth == 0)
        {
            chains.Add(path.ToList());
            GraphTraversal.Leave(visiting, current);
            return;
        }

        var callees = GetCallees(current);
        if (callees.Count == 0)
        {
            chains.Add(path.ToList());
            GraphTraversal.Leave(visiting, current);
            return;
        }

        var extended = false;
        foreach (var callee in callees)
        {
            if (GraphTraversal.IsInPath(path, callee))
                continue;

            path.Add(callee);
            extended = true;
            DfsCallChain(callee, remainingDepth - 1, path, chains, visiting);
            path.RemoveAt(path.Count - 1);
        }

        if (!extended)
            chains.Add(path.ToList());

        GraphTraversal.Leave(visiting, current);
    }

    private void EnsureExists(string methodId)
    {
        if (!Contains(methodId))
            throw new KeyNotFoundException($"Method node not found: {methodId}");
    }
}

// =============================================================================
// Graph/GraphQueryService.cs — 代码图查询服务（只读）
// =============================================================================
// 【边界】只依赖 GraphIndex；不修改图；不调用 Builder / Semantic。
// API：GetCallers / GetCallees / GetCallChain / FindEntryPoints
// =============================================================================

using Core.Graph.Indexing;
using Core.Graph.Query;
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

    public IEnumerable<GraphNode> GetAllNodes() => _index.Nodes.Values;

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

    // ═══════════════════════════════════════════════════════════════
    // Semantic Query APIs (Query 2.0)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 查找访问指定表的所有 Route 入口路径。
    /// Route → Controller → Service → Repository → Entity → Table
    /// </summary>
    public IReadOnlyList<SemanticPath> FindRoutesToTable(string tableName)
    {
        var entityNodeIds = FindEntityNodesByTable(tableName);
        if (entityNodeIds.Count == 0)
            return Array.Empty<SemanticPath>();

        var options = SemanticTraversalOptions.TableImpact(tableName);
        return SemanticTraversalEngine.Traverse(_index, entityNodeIds, options);
    }

    /// <summary>
    /// 从 Table 反向查找所有访问入口。
    /// Table → Entity → Repository → Service → Controller → Route
    /// </summary>
    public IReadOnlyList<SemanticPath> FindTableImpact(string tableName)
    {
        var entityNodeIds = FindEntityNodesByTable(tableName);
        if (entityNodeIds.Count == 0)
            return Array.Empty<SemanticPath>();

        var options = SemanticTraversalOptions.TableImpact(tableName);
        return SemanticTraversalEngine.Traverse(_index, entityNodeIds, options);
    }

    /// <summary>
    /// 查找访问特定 Entity 的所有 API 方法。
    /// </summary>
    public IReadOnlyList<SemanticPath> FindApisByEntity(string entityClass)
    {
        var entityNodeIds = FindEntityNodesByClass(entityClass);
        if (entityNodeIds.Count == 0)
            return Array.Empty<SemanticPath>();

        var options = SemanticTraversalOptions.TableImpact(entityClass);
        return SemanticTraversalEngine.Traverse(_index, entityNodeIds, options);
    }

    /// <summary>
    /// 查找访问指定表的所有 Repository 方法。
    /// </summary>
    public IReadOnlyList<SemanticPath> FindRepositoriesByTable(string tableName)
    {
        var entityNodeIds = FindEntityNodesByTable(tableName);
        if (entityNodeIds.Count == 0)
            return Array.Empty<SemanticPath>();

        var opts = new SemanticTraversalOptions
        {
            EdgeKinds = new HashSet<string>(StringComparer.Ordinal) { "nh:entity-access" },
            Direction = TraversalDirection.Backward,
            MaxDepth = 1,
            MaxPaths = 100
        };

        return SemanticTraversalEngine.Traverse(_index, entityNodeIds, opts);
    }

    /// <summary>
    /// 分析某个方法的影响面（上游调用链 → 下游数据链）。
    /// </summary>
    public IReadOnlyList<SemanticPath> FindImpactByMethod(string methodId)
    {
        EnsureExists(methodId);

        var options = new SemanticTraversalOptions
        {
            EdgeKinds = new HashSet<string>(StringComparer.Ordinal)
                { "call", "spring:implements", "spring:property-ref", "nh:entity-access" },
            Direction = TraversalDirection.Both,
            MaxDepth = 8,
            MaxPaths = 50
        };

        return SemanticTraversalEngine.Traverse(_index, new[] { methodId }, options);
    }

    /// <summary>
    /// 通用多跳语义路径查询。
    /// </summary>
    public IReadOnlyList<SemanticPath> FindSemanticPath(
        string fromId,
        string toId,
        SemanticTraversalOptions options)
    {
        EnsureExists(fromId);
        EnsureExists(toId);

        var opt = new SemanticTraversalOptions
        {
            EdgeKinds = options.EdgeKinds,
            NodeKinds = options.NodeKinds,
            Direction = options.Direction,
            MinConfidence = options.MinConfidence,
            MaxDepth = options.MaxDepth ?? 15,
            MaxPaths = options.MaxPaths,
            DeduplicatePaths = options.DeduplicatePaths,
            TargetAttributeKey = "nh-entity-access"  // 匹配 ID target (通过 StopAtNode)
        };

        return SemanticTraversalEngine.Traverse(_index, new[] { fromId }, opt);
    }

    /// <summary>
    /// 查找图中所有节点里 Attributes["nh:table"] 匹配指定表的 Entity 节点。
    /// </summary>
    public IReadOnlyList<string> FindEntityNodesByTable(string tableName)
    {
        return _index.Nodes.Keys
            .Where(id => id.StartsWith("ext::nh:entity", StringComparison.Ordinal)
                         && id.Contains("::" + tableName, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// 查找图中所有 Attributes["nh:entity-class"] 匹配的 Entity 节点。
    /// </summary>
    public IReadOnlyList<string> FindEntityNodesByClass(string entityClass)
    {
        return _index.Nodes.Keys
            .Where(id => id.StartsWith("ext::nh:entity", StringComparison.Ordinal)
                         && id.Contains("." + entityClass + "::", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 查找标注为 entry-point 的方法节点。
    /// </summary>
    public IReadOnlyList<string> FindEntryPointNodes()
    {
        return _index.Nodes.Values
            .Where(n => n.Attributes.ContainsKey("aspnet-route:entry-point"))
            .Select(n => n.Id)
            .ToList();
    }

    public EdgeInfo? GetEdgeInfo(string fromId, string toId)
    {
        if (!_index.EdgeIdx.OutgoingByKind.TryGetValue(fromId, out var edges))
            return null;

        foreach (var edge in edges)
        {
            if (edge.ToId == toId)
                return edge;
        }

        return null;
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

// =============================================================================
// Graph/Traversal/GraphTraversal.cs — 图遍历防环工具
// =============================================================================
// 供 GraphQueryService 在 DFS/BFS 时使用，避免循环调用图死循环。
// =============================================================================

namespace Core.Graph.Traversal;

internal static class GraphTraversal
{
    /// <summary>尝试进入节点；若已在 visiting 中则返回 false（检测到环）。</summary>
    public static bool TryEnter(HashSet<string> visiting, string nodeId) => visiting.Add(nodeId);

    public static void Leave(HashSet<string> visiting, string nodeId) => visiting.Remove(nodeId);

    /// <summary>当前路径中是否已包含该节点（另一层环检测）。</summary>
    public static bool IsInPath(IReadOnlyCollection<string> path, string nodeId) =>
        path.Contains(nodeId, StringComparer.Ordinal);
}

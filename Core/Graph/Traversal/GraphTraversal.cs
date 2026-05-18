namespace Core.Graph.Traversal;

internal static class GraphTraversal
{
    public static bool TryEnter(HashSet<string> visiting, string nodeId) => visiting.Add(nodeId);

    public static void Leave(HashSet<string> visiting, string nodeId) => visiting.Remove(nodeId);

    public static bool IsInPath(IReadOnlyCollection<string> path, string nodeId) =>
        path.Contains(nodeId, StringComparer.Ordinal);
}

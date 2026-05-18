// =============================================================================
// Graph/CodeGraphBuildResult.cs — 建图 + 索引构建的最终产物
// =============================================================================

using Core.Graph.Indexing;

namespace Core.Graph;

/// <summary>
/// 包含可序列化的图（CodeGraph）与用于查询的邻接索引（GraphIndex）。
/// </summary>
public sealed class CodeGraphBuildResult
{
    public required CodeGraph Graph { get; init; }

    public required GraphIndex Index { get; init; }
}

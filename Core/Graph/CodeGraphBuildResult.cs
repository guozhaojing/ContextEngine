// =============================================================================
// Graph/CodeGraphBuildResult.cs — 建图 + 索引构建的最终产物
// =============================================================================

using Core.Graph.Indexing;
using Core.Semantics;

namespace Core.Graph;

/// <summary>
/// 包含可序列化的图（CodeGraph）、用于查询的邻接索引（GraphIndex）以及符号索引。
/// </summary>
public sealed class CodeGraphBuildResult
{
    public required CodeGraph Graph { get; init; }

    public required GraphIndex Index { get; init; }

    public SymbolReferenceIndex? SymbolIndex { get; init; }
}

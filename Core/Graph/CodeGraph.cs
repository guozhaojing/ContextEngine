// =============================================================================
// Graph/CodeGraph.cs — 代码调用图（纯数据结构）
// =============================================================================

using Core.Graph.Analysis;

namespace Core.Graph;

/// <summary>
/// 内存中的完整代码图：节点（方法）+ 边（调用）+ 分析事实。
/// </summary>
public class CodeGraph
{
    public string ScanRoot { get; set; } = "";

    /// <summary>图结构版本号，便于以后序列化/增量合并。</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<GraphNode> Nodes { get; set; } = new();

    public List<GraphEdge> Edges { get; set; } = new();

    /// <summary>由 Graph.Analysis 分析器写入的结构化事实（与节点 Attributes 互补）。</summary>
    public List<GraphFact> Facts { get; set; } = new();

    public int ResolvedEdgeCount => Edges.Count(e => e.IsResolved);

    public int ExternalNodeCount => Nodes.Count(n => n.IsExternal);
}

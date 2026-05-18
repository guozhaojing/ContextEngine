// =============================================================================
// Graph/Analysis/GraphAnalysisResult.cs — 单个分析器的产出
// =============================================================================

namespace Core.Graph.Analysis;

public sealed class GraphAnalysisResult
{
    public string AnalyzerName { get; init; } = "";

    public List<GraphFact> Facts { get; } = [];

    public List<GraphAnnotation> Annotations { get; } = [];

    public List<GraphExtraEdge> ExtraEdges { get; } = [];
}

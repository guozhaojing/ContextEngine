// =============================================================================
// Graph/Analysis/IGraphAnalyzer.cs — 可插拔图分析器契约
// =============================================================================
// 实现类示例（未来）：AspNetRouteAnalyzer、EfSqlAnalyzer、MediatRAnalyzer
// 禁止：依赖 GraphQueryService、修改 CodeGraphBuilder、直接改 GraphNode
// =============================================================================

namespace Core.Graph.Analysis;

public interface IGraphAnalyzer
{
    /// <summary>分析器唯一名称，用于增量合并时按名清除旧数据。</summary>
    string Name { get; }

    /// <summary>
    /// 执行分析，通过 context.AddFact / AddAnnotation / AddExtraEdge 产出结果。
    /// </summary>
    void Analyze(GraphAnalysisContext context);
}

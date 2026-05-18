// =============================================================================
// Graph/Analysis/GraphAnalysisPipeline.cs — 按顺序执行所有 IGraphAnalyzer
// =============================================================================

using Core.Scanning;

namespace Core.Graph.Analysis;

/// <summary>
/// 分析器管道：每个分析器独立 Context，产出汇总为 GraphAnalysisRunResult。
/// </summary>
public sealed class GraphAnalysisPipeline{
    private readonly IReadOnlyList<IGraphAnalyzer> _analyzers;

    public GraphAnalysisPipeline(IEnumerable<IGraphAnalyzer> analyzers)
    {
        _analyzers = analyzers.ToList();
    }

    public IReadOnlyList<IGraphAnalyzer> Analyzers => _analyzers;

    public GraphAnalysisRunResult Run(
        SolutionScanResult scan,
        CodeGraph baseGraph,
        GraphAnalysisScope scope)
    {
        var results = new List<GraphAnalysisResult>(_analyzers.Count);

        foreach (var analyzer in _analyzers)
        {
            var context = GraphAnalysisContext.Create(scan, baseGraph, scope, analyzer);
            analyzer.Analyze(context);
            results.Add(context.Result);
        }

        return new GraphAnalysisRunResult(results);
    }
}

using Core.Scanning;

namespace Core.Graph.Analysis;

/// <summary>
/// 编排：基础图构建 → 分析器管道 → 合并 → 重建索引。
/// </summary>
public sealed class CodeGraphAnalysisOrchestrator
{
    private readonly GraphAnalysisPipeline _pipeline;
    private readonly GraphAnalysisMergeService _mergeService = new();

    public CodeGraphAnalysisOrchestrator(IEnumerable<IGraphAnalyzer> analyzers)
    {
        _pipeline = new GraphAnalysisPipeline(analyzers);
    }

    public CodeGraphBuildResult BuildAndAnalyze(
        SolutionScanResult scan,
        GraphAnalysisScope? scope = null)
    {
        scope ??= GraphAnalysisScope.Full();

        var baseBuild = CodeGraphBuilder.Build(scan);
        var analysisRun = _pipeline.Run(scan, baseBuild.Graph, scope);
        return _mergeService.Merge(baseBuild.Graph, analysisRun, scope);
    }
}

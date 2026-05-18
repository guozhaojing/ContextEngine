using Core.Scanning;

namespace Core.Graph.Analysis;

public sealed class GraphAnalysisPipeline
{
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

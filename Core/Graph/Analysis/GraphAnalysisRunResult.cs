// =============================================================================
// Graph/Analysis/GraphAnalysisRunResult.cs — 管道执行后的汇总
// =============================================================================

namespace Core.Graph.Analysis;

public sealed class GraphAnalysisRunResult
{
    public GraphAnalysisRunResult(IReadOnlyList<GraphAnalysisResult> analyzerResults)
    {
        AnalyzerResults = analyzerResults;
    }

    public IReadOnlyList<GraphAnalysisResult> AnalyzerResults { get; }

    public IEnumerable<GraphFact> AllFacts =>
        AnalyzerResults.SelectMany(result => result.Facts);

    public IEnumerable<GraphAnnotation> AllAnnotations =>
        AnalyzerResults.SelectMany(result => result.Annotations);

    public IEnumerable<GraphExtraEdge> AllExtraEdges =>
        AnalyzerResults.SelectMany(result => result.ExtraEdges);
}

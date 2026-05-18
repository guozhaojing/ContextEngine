namespace Core.Graph.Analysis;

public interface IGraphAnalyzer
{
    string Name { get; }

    void Analyze(GraphAnalysisContext context);
}

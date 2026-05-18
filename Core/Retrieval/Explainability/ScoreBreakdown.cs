namespace Core.Retrieval.Explainability;

public sealed class ScoreBreakdown
{
    public double VectorSimilarity { get; init; }
    public double GraphRelevance { get; init; }
    public double BusinessRelevance { get; init; }
    public double ImportanceScore { get; init; }
    public double FusedScore { get; init; }

    public double VectorWeight => 0.35;
    public double GraphWeight => 0.25;
    public double BusinessWeight => 0.20;
    public double ImportanceWeight => 0.20;
}

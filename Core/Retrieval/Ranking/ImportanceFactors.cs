namespace Core.Retrieval.Ranking;

public sealed class ImportanceFactors
{
    public double CentralityFactor { get; init; }
    public double BusinessFactor { get; init; }
    public double TraversalFactor { get; init; }
    public double EntryPointFactor { get; init; }
    public double EntityAccessFactor { get; init; }
    public double DependencyFactor { get; init; }
    public double FinalScore { get; init; }
}

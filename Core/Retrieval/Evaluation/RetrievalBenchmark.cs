namespace Core.Retrieval.Evaluation;

public sealed class RetrievalBenchmark
{
    public string Name { get; init; } = "";
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public int CaseCount => Cases.Count;
    public required IReadOnlyList<BenchmarkCase> Cases { get; init; }
    public IReadOnlyList<BenchmarkResult> Results { get; init; } = Array.Empty<BenchmarkResult>();
    public AggregateMetrics? Aggregate { get; init; }
}

public sealed class AggregateMetrics
{
    public double AvgRecall { get; init; }
    public double AvgPrecision { get; init; }
    public double AvgMRR { get; init; }
    public double AvgNDCG { get; init; }
    public double HitRate { get; init; }
    public double AvgLayerCoverage { get; init; }
    public double AvgEntityCoverage { get; init; }
    public double AvgRouteCoverage { get; init; }
    public int TotalFailures { get; init; }
    public double AvgSearchTimeMs { get; init; }
}

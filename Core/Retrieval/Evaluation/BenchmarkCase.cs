namespace Core.Retrieval.Evaluation;

public sealed class BenchmarkCase
{
    public required string CaseId { get; init; }
    public required string Query { get; init; }
    public required BenchmarkExpected Expected { get; init; }
    public int TopK { get; init; } = 10;
    public double MinRecall { get; init; } = 0.3;
    public double MinMRR { get; init; } = 0.5;
}

namespace Core.Retrieval.Evaluation;

public sealed class BenchmarkResult
{
    public required BenchmarkCase Case { get; init; }
    public required RetrievalMetrics Metrics { get; init; }
    public IReadOnlyList<string> RetrievedChunkIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> RetrievedScores { get; init; } = Array.Empty<double>();
    public IReadOnlyList<string> MissedExpectedIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<FailureReason> Failures { get; init; } = Array.Empty<FailureReason>();
    public double SearchTimeMs { get; init; }
}

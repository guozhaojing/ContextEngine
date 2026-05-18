namespace Core.Retrieval.Explainability;

public sealed class RetrievalTrace
{
    public string Step { get; init; } = "";
    public string Description { get; init; } = "";
    public double DurationMs { get; init; }
    public int ResultCount { get; init; }
}

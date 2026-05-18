namespace Core.Retrieval.Evaluation;

public sealed class BenchmarkExpected
{
    public IReadOnlyList<string> ChunkIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MethodLabels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EntityNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TableNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LayerNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RoutePatterns { get; init; } = Array.Empty<string>();
}

namespace Core.Retrieval.Retrieval;

public sealed class RetrievalResult
{
    public required RetrievalQuery Query { get; init; }
    public required IReadOnlyList<RetrievalCandidate> Candidates { get; init; }
    public int TotalChunksSearched { get; init; }
    public int VectorCandidates { get; init; }
    public double SearchTimeMs { get; init; }
}

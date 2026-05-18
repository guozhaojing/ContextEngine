using Core.Retrieval.Chunking;

namespace Core.Retrieval.Retrieval;

public sealed class RetrievalCandidate
{
    public required CodeChunk Chunk { get; init; }
    public double VectorSimilarity { get; init; }
    public double GraphRelevance { get; init; }
    public double BusinessRelevance { get; init; }
    public double FusedScore { get; init; }
}

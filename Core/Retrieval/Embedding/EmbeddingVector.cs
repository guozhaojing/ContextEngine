namespace Core.Retrieval.Embedding;

public sealed class EmbeddingVector
{
    public required float[] Values { get; init; }
    public int Dimensions => Values.Length;
    public float Magnitude => MathF.Sqrt(Values.Sum(v => v * v));
}

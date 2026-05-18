using System.Security.Cryptography;
using System.Text;

namespace Core.Retrieval.Embedding;

public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    private const int DefaultDimensions = 384;

    public string ModelName => "fake-hash/deterministic";
    public int Dimensions => DefaultDimensions;

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vector = new float[Dimensions];

        // Deterministic pseudo-vector from hash
        for (var i = 0; i < Dimensions; i++)
        {
            var byteIdx = i % (hash.Length - 2);
            // Use 2 bytes for a stable float in [-1, 1]
            var raw = (float)((hash[byteIdx] << 8 | hash[byteIdx + 1]) - 32768) / 32768f;
            // Clamp away from NaN/Infinity
            if (float.IsNaN(raw) || float.IsInfinity(raw)) raw = 0f;
            vector[i] = Math.Clamp(raw, -1f, 1f);
        }

        // Normalize to unit vector (avoid div by zero)
        var magnitude = 0f;
        for (var i = 0; i < vector.Length; i++)
            magnitude += vector[i] * vector[i];
        magnitude = MathF.Sqrt(magnitude);
        if (magnitude > 1e-8f)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= magnitude;
        }

        return Task.FromResult(vector);
    }
}

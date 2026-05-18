// =============================================================================
// Models/ContextCompressionResult.cs — per-item compression metadata
// =============================================================================

namespace Core.Context.Models;

public sealed class ContextCompressionResult
{
    public required string OriginalContent { get; init; }
    public required string CompressedContent { get; init; }
    public int OriginalTokens { get; init; }
    public int CompressedTokens { get; init; }
    public double CompressionRatio =>
        OriginalTokens > 0 ? (double)CompressedTokens / OriginalTokens : 1.0;
    public required string Strategy { get; init; }
    public IReadOnlyList<string> SourceChunkIds { get; init; } = Array.Empty<string>();
}

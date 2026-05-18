// =============================================================================
// Models/StructuredContext.cs — LLM-ready structured context output
// =============================================================================

namespace Core.Context.Models;

public sealed class StructuredContext
{
    public required string Query { get; init; }
    public string Intent { get; init; } = "";
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> SemanticPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Routes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BusinessRules { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CompressedMethods { get; init; } = Array.Empty<string>();
    public int TokenEstimate { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

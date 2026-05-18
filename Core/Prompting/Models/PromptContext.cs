// =============================================================================
// Models/PromptContext.cs — prompt-ready context for LLM consumption
// =============================================================================

namespace Core.Prompting.Models;

public sealed class PromptContext
{
    public required string UserQuery { get; init; }
    public required string DetectedIntent { get; init; }
    public string Summary { get; init; } = "";
    public required IReadOnlyList<PromptSection> ReasoningSections { get; init; }
    public IReadOnlyList<string> SemanticPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ImportantMethods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BusinessRules { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Constraints { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MissingContextIssue> MissingContextIssues { get; init; } = Array.Empty<MissingContextIssue>();
    public int TokenEstimate { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

// =============================================================================
// Models/PromptMetadata.cs — metadata for prompt context assembly
// =============================================================================

namespace Core.Prompting.Models;

public sealed class PromptMetadata
{
    public required string PromptId { get; init; }
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public int SchemaVersion { get; init; } = 1;
    public int TotalTokens { get; init; }
    public int BudgetTokens { get; init; }
    public int SectionCount { get; init; }
    public int MissingIssueCount { get; init; }
    public double AverageRelevance { get; init; }
    public double AverageCompression { get; init; }
    public string QueryIntent { get; init; } = "";
    public string PrioritizationStrategy { get; init; } = "";
    public IReadOnlyDictionary<string, string> Extra { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

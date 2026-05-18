// =============================================================================
// Models/FinalPrompt.cs — the rendered prompt ready for LLM consumption
// =============================================================================

using Core.Context.Models;

namespace Core.Prompting.Models;

public sealed class FinalPrompt
{
    public required string PromptId { get; init; }
    public required string Query { get; init; }
    public required string IntentSummary { get; init; }
    public required string Content { get; init; }
    public required IReadOnlyList<PromptSection> Sections { get; init; }
    public CodeAnchors Anchors { get; init; } = new();
    public required string ExecutionPlan { get; init; }
    public required string ExpectedOutputFormat { get; init; }
    public IReadOnlyList<string> Constraints { get; init; } = Array.Empty<string>();
    public int TokenEstimate { get; init; }
    public required string StrategyName { get; init; }
    public required string TemplateName { get; init; }
    public required string PolicyName { get; init; }
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed class CodeAnchors
{
    public IReadOnlyList<string> Methods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Routes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Entities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();
}

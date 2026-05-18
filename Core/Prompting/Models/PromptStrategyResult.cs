// =============================================================================
// Models/PromptStrategyResult.cs — result from a prompt strategy execution
// =============================================================================

namespace Core.Prompting.Models;

public sealed class PromptStrategyResult
{
    public required string StrategyName { get; init; }
    public required FinalPrompt FinalPrompt { get; init; }
    public required PromptTemplate Template { get; init; }
    public required ContextPolicies.ContextPolicy Policy { get; init; }
    public IReadOnlyDictionary<string, string> Decisions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

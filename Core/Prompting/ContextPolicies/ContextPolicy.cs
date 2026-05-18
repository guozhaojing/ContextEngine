// =============================================================================
// ContextPolicies/ContextPolicy.cs — defines how context is assembled for a prompt
// =============================================================================

using Core.Prompting.Models;

namespace Core.Prompting.ContextPolicies;

public sealed class ContextPolicy
{
    public required string PolicyId { get; init; }
    public required string PolicyName { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<PolicySectionRule> SectionRules { get; init; }
    public int MaxTokens { get; init; } = 8000;
    public required IReadOnlyList<string> FocusAreas { get; init; }
    public bool IncludeMissingContext { get; init; } = true;
    public bool IncludeExecutionPlan { get; init; } = true;
    public string OutputFormatHint { get; init; } = "";
}

public sealed class PolicySectionRule
{
    public required PromptSectionKind SectionKind { get; init; }
    public bool Required { get; init; } = true;
    public int MaxItems { get; init; } = 20;
    public int MaxTokens { get; init; } = 2000;
    public double MinRelevance { get; init; } = 0;
    public int Order { get; init; } = 50;
}

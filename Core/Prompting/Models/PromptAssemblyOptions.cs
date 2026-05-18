// =============================================================================
// Models/PromptAssemblyOptions.cs — prompt assembly configuration
// =============================================================================

namespace Core.Prompting.Models;

public sealed class PromptAssemblyOptions
{
    public int MaxPromptTokens { get; init; } = 8000;
    public bool EnableMissingContextDetection { get; init; } = true;
    public bool EnableCompactMode { get; init; } = false;
    public bool EnableDetailedMode { get; init; } = true;
    public int MaxPathsPerSection { get; init; } = 15;
    public int MaxMethodsPerSection { get; init; } = 20;
    public int MaxRulesPerSection { get; init; } = 15;
    public int MaxEntitiesPerSection { get; init; } = 30;
    public int MaxIssuesPerSection { get; init; } = 10;

    public static readonly PromptAssemblyOptions Default = new();
}

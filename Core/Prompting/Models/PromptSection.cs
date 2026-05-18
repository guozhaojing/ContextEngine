// =============================================================================
// Models/PromptSection.cs — a single reasoning section in the prompt
// =============================================================================

namespace Core.Prompting.Models;

public sealed class PromptSection
{
    public required string SectionId { get; init; }
    public required string Title { get; init; }
    public required PromptSectionKind Kind { get; init; }
    public required string Content { get; init; }
    public int Priority { get; init; } = 5;
    public int TokenEstimate { get; init; }
    public double RelevanceScore { get; init; }
    public double CompressionRatio { get; init; } = 1.0;
    public IReadOnlyList<string> SourceChunkIds { get; init; } = Array.Empty<string>();
}

public enum PromptSectionKind
{
    UserIntent,
    BusinessContext,
    RelevantRoutes,
    SemanticPaths,
    ImportantMethods,
    BusinessRules,
    EntitiesTables,
    Constraints,
    MissingInformation,
    Summary
}

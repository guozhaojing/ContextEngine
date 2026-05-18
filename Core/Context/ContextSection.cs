// =============================================================================
// ContextSection.cs — a single section of structured context
// =============================================================================

namespace Core.Context;

public sealed class ContextSection
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required ContextSectionKind Kind { get; init; }
    public int Priority { get; init; } = 5;
    public int TokenCount { get; init; }
    public IReadOnlyList<string> SourceChunkIds { get; init; } = Array.Empty<string>();
    public double CompressionRatio { get; init; } = 1.0;
    public double RelevanceScore { get; init; }
}

public enum ContextSectionKind
{
    RouteChain,
    EntityAccess,
    CallChain,
    EntityTableSummary,
    VariableUsage,
    BusinessRule,
    StructuredSummary,
    EntryPointDetail,
    SemanticPath,
    CompressedMethod,
    RouteSummary
}

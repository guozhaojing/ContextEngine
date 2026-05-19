// =============================================================================
// ContextSection.cs — a single section of structured context
// =============================================================================
// v2: 新增 Evidence / SourceSymbolHandles / SourceNodeIds / IsGrounded
//     section title/source 必须来源于 graph node/chunk/symbol，禁止 LLM 自由命名。
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

    // ── v2: Grounding fields ──

    /// <summary>section 产生的证据轨迹。</summary>
    public SectionEvidence? Evidence { get; set; }

    /// <summary>来自哪些 graph node（可 trace 回 source file）。</summary>
    public IReadOnlyList<string> SourceNodeIds { get; set; } = Array.Empty<string>();

    /// <summary>来自哪些符号（SymbolHandle）。</summary>
    public IReadOnlyList<string> SourceSymbolHandles { get; set; } = Array.Empty<string>();

    /// <summary>section 是否完全 grounded（所有内容可追溯）。</summary>
    public bool IsGrounded { get; set; }
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

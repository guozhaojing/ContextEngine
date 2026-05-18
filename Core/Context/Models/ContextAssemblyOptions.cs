// =============================================================================
// Models/ContextAssemblyOptions.cs — assembly pipeline configuration
// =============================================================================

namespace Core.Context.Models;

public sealed class ContextAssemblyOptions
{
    public int MaxTokens { get; init; } = 12000;
    public bool EnableSemanticCompression { get; init; } = true;
    public bool EnableRedundancyReduction { get; init; } = true;
    public bool EnableBusinessRuleExtraction { get; init; } = true;
    public int MaxSemanticPaths { get; init; } = 20;
    public int MaxRoutes { get; init; } = 10;
    public int MaxEntities { get; init; } = 50;
    public int MaxTables { get; init; } = 30;
    public int MaxBusinessRules { get; init; } = 25;
    public int MaxCompressedMethods { get; init; } = 40;
    public int MethodCompressionMaxLines { get; init; } = 60;

    public static readonly ContextAssemblyOptions Default = new();
}

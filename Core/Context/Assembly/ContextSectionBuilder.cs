// =============================================================================
// Assembly/ContextSectionBuilder.cs — builds explainable ContextSection instances
// =============================================================================

using Core.Context.Budgeting;
using Core.Context.Compression;
using Core.Context.Models;
using Core.Retrieval.Chunking;
using Core.Retrieval.Retrieval;

namespace Core.Context.Assembly;

public sealed class ContextSectionBuilder
{
    private readonly ContextAssemblyOptions _options;

    public ContextSectionBuilder(ContextAssemblyOptions? options = null)
    {
        _options = options ?? ContextAssemblyOptions.Default;
    }

    public ContextSection BuildRouteSection(IReadOnlyList<string> routes, IReadOnlyList<string> chunkIds)
    {
        var content = string.Join('\n', routes);
        return new ContextSection
        {
            Title = "Routes",
            Content = content,
            Kind = ContextSectionKind.RouteSummary,
            Priority = 10,
            TokenCount = ContextBudgetEstimator.Estimate(content),
            SourceChunkIds = chunkIds,
            CompressionRatio = 1.0,
            RelevanceScore = routes.Count > 0 ? 1.0 : 0
        };
    }

    public ContextSection BuildPathSection(IReadOnlyList<string> paths, IReadOnlyList<string> chunkIds)
    {
        var content = string.Join('\n', paths);
        return new ContextSection
        {
            Title = "Semantic Paths",
            Content = content,
            Kind = ContextSectionKind.SemanticPath,
            Priority = 9,
            TokenCount = ContextBudgetEstimator.Estimate(content),
            SourceChunkIds = chunkIds,
            CompressionRatio = 1.0,
            RelevanceScore = paths.Count > 0 ? 1.0 : 0
        };
    }

    public ContextSection BuildEntityTableSection(
        IReadOnlyList<string> entities,
        IReadOnlyList<string> tables,
        IReadOnlyList<string> chunkIds)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Entities:");
        foreach (var e in entities) sb.AppendLine($"  {e}");
        sb.AppendLine();
        sb.AppendLine("Tables:");
        foreach (var t in tables) sb.AppendLine($"  {t}");

        var content = sb.ToString().TrimEnd();
        return new ContextSection
        {
            Title = "Entities & Tables",
            Content = content,
            Kind = ContextSectionKind.EntityTableSummary,
            Priority = 8,
            TokenCount = ContextBudgetEstimator.Estimate(content),
            SourceChunkIds = chunkIds,
            CompressionRatio = 1.0,
            RelevanceScore = (entities.Count + tables.Count) > 0 ? 1.0 : 0
        };
    }

    public ContextSection BuildBusinessRuleSection(
        IReadOnlyList<string> rules,
        string originalContent,
        IReadOnlyList<string> chunkIds)
    {
        var content = string.Join('\n', rules);
        var originalTokens = ContextBudgetEstimator.Estimate(originalContent);
        var compressedTokens = ContextBudgetEstimator.Estimate(content);
        var ratio = originalTokens > 0 ? (double)compressedTokens / originalTokens : 1.0;

        return new ContextSection
        {
            Title = "Business Rules",
            Content = content,
            Kind = ContextSectionKind.BusinessRule,
            Priority = 6,
            TokenCount = compressedTokens,
            SourceChunkIds = chunkIds,
            CompressionRatio = ratio,
            RelevanceScore = rules.Count > 0 ? 1.0 : 0
        };
    }

    public ContextSection BuildCompressedMethodSection(
        ContextCompressionResult cr,
        double relevanceScore)
    {
        return new ContextSection
        {
            Title = "Compressed Method",
            Content = cr.CompressedContent,
            Kind = ContextSectionKind.CompressedMethod,
            Priority = 5,
            TokenCount = cr.CompressedTokens,
            SourceChunkIds = cr.SourceChunkIds,
            CompressionRatio = cr.CompressionRatio,
            RelevanceScore = relevanceScore
        };
    }

    public ContextSection BuildSummarySection(
        string summary,
        IReadOnlyList<string> chunkIds,
        int candidateCount)
    {
        return new ContextSection
        {
            Title = "Summary",
            Content = summary,
            Kind = ContextSectionKind.StructuredSummary,
            Priority = 3,
            TokenCount = ContextBudgetEstimator.Estimate(summary),
            SourceChunkIds = chunkIds,
            CompressionRatio = 1.0,
            RelevanceScore = candidateCount > 0 ? 1.0 : 0
        };
    }

    public ContextSection BuildEntryPointSection(
        CodeChunk routeChunk,
        RetrievalCandidate candidate)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Entry Point: {routeChunk.Title}");
        sb.AppendLine($"Relevance: {candidate.FusedScore:F3}");
        sb.AppendLine($"Importance: {routeChunk.ImportanceScore:F1}/10");

        if (routeChunk.Metadata is not null)
        {
            var m = routeChunk.Metadata;
            if (m.RelatedTables.Count > 0)
                sb.AppendLine($"Tables: {string.Join(", ", m.RelatedTables)}");
            if (m.RelatedEntities.Count > 0)
                sb.AppendLine($"Entities: {string.Join(", ", m.RelatedEntities)}");
            sb.AppendLine($"Data distance: {m.DataAccessDistance}h");
        }

        var content = sb.ToString();
        return new ContextSection
        {
            Title = $"Entry: {routeChunk.Title}",
            Content = content,
            Kind = ContextSectionKind.EntryPointDetail,
            Priority = 10,
            TokenCount = ContextBudgetEstimator.Estimate(content),
            SourceChunkIds = new[] { routeChunk.ChunkId },
            CompressionRatio = 1.0,
            RelevanceScore = candidate.FusedScore
        };
    }
}

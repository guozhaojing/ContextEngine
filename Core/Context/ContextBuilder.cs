using System.Text;
using Core.Context.Compression;
using Core.Context.Models;
using Core.Graph;
using Core.Graph.Query;
using Core.Retrieval.Chunking;
using Core.Retrieval.Retrieval;
using Core.Retrieval.Embedding;

namespace Core.Context;

public sealed class ContextBuilder
{
    private readonly GraphQueryService _query;
    private readonly ContextCompression _compression;

    public ContextBuilder(GraphQueryService query)
    {
        _query = query;
        _compression = new ContextCompression(query);
    }

    internal GraphQueryService QueryService => _query;

    public IReadOnlyList<ContextSection> BuildSections(RetrievalResult retrievalResult)
    {
        var sections = new List<ContextSection>();
        if (retrievalResult.Candidates.Count == 0) return sections;

        var chunks = retrievalResult.Candidates.Select(c => c.Chunk).ToList();

        var routeChunks = chunks.Where(c => c.Kind == ChunkKind.Route).ToList();
        foreach (var routeChunk in routeChunks)
        {
            sections.Add(BuildEntryPointSection(routeChunk));
        }

        sections.Add(BuildRouteChainSection(chunks));

        sections.Add(BuildEntityTableSection(chunks));

        var pathChunks = chunks.Where(c => c.Kind == ChunkKind.SemanticPath).ToList();
        if (pathChunks.Count > 0)
        {
            sections.Add(BuildCallChainSection(pathChunks));
        }

        sections.Add(BuildBusinessRuleSection(chunks));

        sections.Add(BuildVariableSection(chunks));

        var methodChunks = chunks.Where(c => c.Kind == ChunkKind.Method).ToList();
        if (methodChunks.Count > 0)
        {
            sections.Add(BuildCompressedMethodsSection(methodChunks, retrievalResult));
        }

        sections.Add(BuildStructuredSummarySection(chunks, retrievalResult));

        return sections;
    }

    private ContextSection BuildEntryPointSection(CodeChunk routeChunk)
    {
        var content = new StringBuilder();
        content.AppendLine($"**Entry Point**: {routeChunk.Title}");
        content.AppendLine();
        content.AppendLine($"**Importance**: {routeChunk.ImportanceScore:F1}/10");
        content.AppendLine();

        if (routeChunk.Metadata is not null)
        {
            var m = routeChunk.Metadata;
            content.AppendLine($"**Connected Tables**: {(m.RelatedTables.Count > 0 ? string.Join(", ", m.RelatedTables) : "none")}");
            content.AppendLine($"**Connected Entities**: {(m.RelatedEntities.Count > 0 ? string.Join(", ", m.RelatedEntities) : "none")}");
            content.AppendLine($"**Data Access Distance**: {m.DataAccessDistance}h");
            content.AppendLine($"**Fan-in**: {m.FanIn}, **Fan-out**: {m.FanOut}");
        }

        var text = content.ToString();
        var candidate = FindCandidate(routeChunk.ChunkId, retrievalResult: null);
        return new ContextSection
        {
            Title = $"Entry Point: {routeChunk.Title}",
            Content = text,
            Kind = ContextSectionKind.EntryPointDetail,
            Priority = 10,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = new[] { routeChunk.ChunkId },
            CompressionRatio = 1.0,
            RelevanceScore = routeChunk.ImportanceScore / 10.0
        };
    }

    private ContextSection BuildRouteChainSection(List<CodeChunk> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Full invocation chain from API entry to database:");
        sb.AppendLine();

        var routeChunks = chunks.Where(c => c.Kind == ChunkKind.Route).ToList();
        foreach (var rc in routeChunks)
        {
            sb.AppendLine($"Entry: {rc.Title}");
        }

        var pathChunks = chunks.Where(c => c.Kind == ChunkKind.SemanticPath).ToList();
        foreach (var pc in pathChunks.Take(3))
        {
            sb.AppendLine($"  {pc.Summary}");
        }

        var text = sb.ToString();
        return new ContextSection
        {
            Title = "Route Chain",
            Content = text,
            Kind = ContextSectionKind.RouteChain,
            Priority = 9,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = pathChunks.Select(c => c.ChunkId).ToList(),
            CompressionRatio = 1.0,
            RelevanceScore = pathChunks.Count > 0 ? 1.0 : 0
        };
    }

    private ContextSection BuildEntityTableSection(List<CodeChunk> chunks)
    {
        var entities = new HashSet<string>(StringComparer.Ordinal);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var entityChunks = chunks.Where(c => c.Kind == ChunkKind.EntityAccess).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Entities and their database table mappings:");
        sb.AppendLine();

        foreach (var chunk in entityChunks)
        {
            foreach (var entity in chunk.EntityNames)
            {
                if (!entities.Add(entity)) continue;
                var tablesForEntity = chunk.TableNames
                    .Where(t => !tables.Contains(t))
                    .ToList();

                foreach (var t in tablesForEntity) tables.Add(t);

                sb.AppendLine($"  **{entity}** \u2192 {(tablesForEntity.Count > 0 ? string.Join(", ", tablesForEntity) : "?")}");
                sb.AppendLine($"    Access paths: {chunk.EntryPoints.Count}");
                sb.AppendLine($"    Importance: {chunk.ImportanceScore:F1}");
            }
        }

        if (entityChunks.Count == 0)
        {
            sb.AppendLine("  No explicit entity chunks found.");
        }

        var text = sb.ToString();
        return new ContextSection
        {
            Title = "Entity / Table Mapping",
            Content = text,
            Kind = ContextSectionKind.EntityTableSummary,
            Priority = 8,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = entityChunks.Select(c => c.ChunkId).ToList(),
            CompressionRatio = 1.0,
            RelevanceScore = entityChunks.Count > 0 ? 1.0 : 0
        };
    }

    private ContextSection BuildCallChainSection(List<CodeChunk> pathChunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Detailed call chains:");
        sb.AppendLine();

        var allNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pc in pathChunks)
        {
            foreach (var nid in pc.NodeIds)
                allNodeIds.Add(nid);
        }

        var entityNodes = allNodeIds
            .Select(id => _query.GetNode(id))
            .Where(n => n is not null)
            .Cast<GraphNode>()
            .Where(n => n.Kind == GraphNodeKind.Entity)
            .ToList();

        if (entityNodes.Count > 0)
        {
            var summary = _compression.BuildEntityTableSummary(entityNodes);
            sb.AppendLine("  **Data mappings:**");
            sb.AppendLine(summary);
            sb.AppendLine();
        }

        foreach (var pc in pathChunks.Take(5))
        {
            sb.AppendLine($"  [{pc.NodeIds.Count - 1}h] {pc.Summary}");
        }

        if (pathChunks.Count > 5)
            sb.AppendLine($"  ... and {pathChunks.Count - 5} more paths");

        var text = sb.ToString();
        return new ContextSection
        {
            Title = "Call Chain Details",
            Content = text,
            Kind = ContextSectionKind.CallChain,
            Priority = 7,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = pathChunks.Select(c => c.ChunkId).ToList(),
            CompressionRatio = 1.0,
            RelevanceScore = pathChunks.Count > 0 ? 1.0 : 0
        };
    }

    private ContextSection BuildBusinessRuleSection(List<CodeChunk> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extracted business rules and constraints:");
        sb.AppendLine();

        var sourceChunkIds = new List<string>();
        var totalRules = 0;
        var foundAny = false;

        foreach (var chunk in chunks)
        {
            var cr = BusinessRuleExtractor.Extract(chunk.Content, new[] { chunk.ChunkId });

            if (!string.IsNullOrEmpty(cr.CompressedContent))
            {
                sb.AppendLine($"  From: {chunk.Title}");
                sb.AppendLine(cr.CompressedContent);
                sourceChunkIds.Add(chunk.ChunkId);
                totalRules += cr.CompressedContent.Split('\n').Length;
                foundAny = true;
            }
        }

        if (!foundAny)
            sb.AppendLine("  No explicit business rules detected.");

        var originalContent = string.Join("\n", chunks.Select(c => c.Content));
        var text = sb.ToString();
        var originalTokens = TokenEstimator.Estimate(originalContent);
        var compressedTokens = TokenEstimator.Estimate(text);

        return new ContextSection
        {
            Title = "Business Rules",
            Content = text,
            Kind = ContextSectionKind.BusinessRule,
            Priority = 5,
            TokenCount = compressedTokens,
            SourceChunkIds = sourceChunkIds,
            CompressionRatio = originalTokens > 0 ? (double)compressedTokens / originalTokens : 1.0,
            RelevanceScore = totalRules > 0 ? Math.Min(1.0, totalRules / 20.0) : 0
        };
    }

    private ContextSection BuildVariableSection(List<CodeChunk> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Important parameters and types:");
        sb.AppendLine();

        var relevantNodes = new List<GraphNode>();
        foreach (var chunk in chunks)
        {
            foreach (var nid in chunk.NodeIds.Take(5))
            {
                var node = _query.GetNode(nid);
                if (node is not null) relevantNodes.Add(node);
            }
        }

        var vars = _compression.ExtractVariables(relevantNodes);
        if (string.IsNullOrEmpty(vars))
            sb.AppendLine("  No significant parameters extracted.");
        else
            sb.AppendLine(vars);

        var text = sb.ToString();
        return new ContextSection
        {
            Title = "Parameters & Types",
            Content = text,
            Kind = ContextSectionKind.VariableUsage,
            Priority = 4,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = chunks.Take(3).Select(c => c.ChunkId).ToList(),
            CompressionRatio = 1.0,
            RelevanceScore = string.IsNullOrEmpty(vars) ? 0 : 0.5
        };
    }

    private ContextSection BuildCompressedMethodsSection(
        List<CodeChunk> methodChunks,
        RetrievalResult retrievalResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Compressed method summaries:");
        sb.AppendLine();

        var sourceChunkIds = new List<string>();
        var totalOriginalTokens = 0;
        var totalCompressedTokens = 0;

        foreach (var chunk in methodChunks.OrderByDescending(c => c.ImportanceScore).Take(15))
        {
            var cr = MethodCompressor.Compress(chunk.Content, new[] { chunk.ChunkId });

            if (!string.IsNullOrEmpty(cr.CompressedContent))
            {
                sb.AppendLine($"### {chunk.Title}");
                sb.AppendLine(cr.CompressedContent);
                sb.AppendLine();
                sourceChunkIds.Add(chunk.ChunkId);
                totalOriginalTokens += cr.OriginalTokens;
                totalCompressedTokens += cr.CompressedTokens;
            }
        }

        var text = sb.ToString();
        return new ContextSection
        {
            Title = "Compressed Methods",
            Content = text,
            Kind = ContextSectionKind.CompressedMethod,
            Priority = 6,
            TokenCount = totalCompressedTokens > 0 ? totalCompressedTokens : TokenEstimator.Estimate(text),
            SourceChunkIds = sourceChunkIds,
            CompressionRatio = totalOriginalTokens > 0 ? (double)totalCompressedTokens / totalOriginalTokens : 1.0,
            RelevanceScore = methodChunks.Count > 0 ? 1.0 : 0
        };
    }

    private ContextSection BuildStructuredSummarySection(
        List<CodeChunk> chunks,
        RetrievalResult retrievalResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Query: " + retrievalResult.Query.Query);
        sb.AppendLine();
        sb.AppendLine($"Top {chunks.Count} relevant code contexts:");
        sb.AppendLine();

        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            var score = i < retrievalResult.Candidates.Count
                ? retrievalResult.Candidates[i].FusedScore
                : 0;
            sb.AppendLine($"  #{i + 1} [{score:F3}] **[{c.Kind}]** {c.Title}");

            if (c.Metadata is not null)
            {
                var m = c.Metadata;
                var tags = new List<string>();
                if (m.IsEntryPoint) tags.Add("entry");
                if (m.IsEntityAccess) tags.Add("data");
                if (tags.Count > 0) sb.AppendLine($"       Tags: {string.Join(", ", tags)}");
            }

            var summary = c.Summary;
            if (summary.Length > 100) summary = summary[..97] + "...";
            sb.AppendLine($"       {summary}");
        }

        var text = sb.ToString();
        return new ContextSection
        {
            Title = "Structured Summary",
            Content = text,
            Kind = ContextSectionKind.StructuredSummary,
            Priority = 3,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = chunks.Select(c => c.ChunkId).ToList(),
            CompressionRatio = 1.0,
            RelevanceScore = chunks.Count > 0 ? 1.0 : 0
        };
    }

    private static RetrievalCandidate? FindCandidate(string chunkId, RetrievalResult? retrievalResult)
    {
        if (retrievalResult is null) return null;
        return retrievalResult.Candidates.FirstOrDefault(c =>
            c.Chunk.ChunkId == chunkId);
    }
}

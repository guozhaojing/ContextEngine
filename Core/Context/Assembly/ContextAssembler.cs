// =============================================================================
// Assembly/ContextAssembler.cs — main assembly pipeline: RetrievalResult → StructuredContext
// =============================================================================

using Core.Context.Budgeting;
using Core.Context.Compression;
using Core.Context.Models;
using Core.Graph;
using Core.Graph.Query;
using Core.Retrieval.Chunking;
using Core.Retrieval.Retrieval;

namespace Core.Context.Assembly;

public sealed class ContextAssembler
{
    private readonly GraphQueryService _query;
    private readonly SemanticPathCompressor _pathCompressor;
    private readonly ContextAssemblyOptions _options;

    public ContextAssembler(GraphQueryService query, ContextAssemblyOptions? options = null)
    {
        _query = query;
        _pathCompressor = new SemanticPathCompressor(query);
        _options = options ?? ContextAssemblyOptions.Default;
    }

    public StructuredContext Assemble(RetrievalResult retrievalResult)
    {
        var budget = new TokenBudgetAllocator(_options.MaxTokens);
        var candidates = retrievalResult.Candidates;
        if (candidates.Count == 0)
        {
            return new StructuredContext
            {
                Query = retrievalResult.Query.Query,
                Intent = "no_results",
                Summary = "No relevant code contexts found.",
                TokenEstimate = 0,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["candidates"] = "0",
                    ["budget_total"] = _options.MaxTokens.ToString()
                }
            };
        }

        var routeChunks = candidates.Where(c => c.Chunk.Kind == ChunkKind.Route).ToList();
        var pathChunks = candidates.Where(c => c.Chunk.Kind == ChunkKind.SemanticPath).ToList();
        var entityChunks = candidates.Where(c => c.Chunk.Kind == ChunkKind.EntityAccess).ToList();
        var methodChunks = candidates.Where(c => c.Chunk.Kind == ChunkKind.Method).ToList();
        var allChunks = candidates.Select(c => c.Chunk).ToList();

        var routes = BuildRoutes(routeChunks, budget);
        var semanticPaths = BuildSemanticPaths(pathChunks, budget);
        var (entities, tables) = BuildEntityTableMappings(entityChunks, budget);
        var businessRules = BuildBusinessRules(allChunks, budget);
        var compressedMethods = BuildCompressedMethods(methodChunks, budget);

        var (redPaths, redMethods, redEntities, redTables, redRules, origCount, redCount) =
            _options.EnableRedundancyReduction
                ? RedundancyReducer.ReduceAll(
                    semanticPaths, compressedMethods, entities, tables, businessRules)
                : (semanticPaths.ToList(), compressedMethods.ToList(), entities.ToList(),
                   tables.ToList(), businessRules.ToList(), 0, 0);

        var intent = InferIntent(retrievalResult.Query.Query, allChunks);
        var summary = BuildSummary(retrievalResult.Query.Query, candidates, redPaths, redEntities, redTables);

        var tokenEstimate = ContextBudgetEstimator.EstimateList(redPaths, ContextBudgetEstimator.OverheadPerPath)
            + ContextBudgetEstimator.EstimateList(routes, ContextBudgetEstimator.OverheadPerRoute)
            + ContextBudgetEstimator.EstimateList(redEntities, ContextBudgetEstimator.OverheadPerEntity)
            + ContextBudgetEstimator.EstimateList(redTables, ContextBudgetEstimator.OverheadPerEntity)
            + ContextBudgetEstimator.EstimateList(redRules, ContextBudgetEstimator.OverheadPerRule)
            + ContextBudgetEstimator.EstimateList(redMethods, ContextBudgetEstimator.OverheadPerCompressedMethod)
            + ContextBudgetEstimator.Estimate(summary) + ContextBudgetEstimator.Estimate(intent);

        budget.Lock();

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidates"] = candidates.Count.ToString(),
            ["budget_total"] = _options.MaxTokens.ToString(),
            ["budget_allocated"] = budget.AllocatedTokens.ToString(),
            ["budget_remaining"] = budget.RemainingTokens.ToString(),
            ["semantic_compression"] = _options.EnableSemanticCompression ? "enabled" : "disabled",
            ["redundancy_reduction"] = _options.EnableRedundancyReduction ? "enabled" : "disabled",
            ["original_items"] = origCount.ToString(),
            ["reduced_items"] = redCount.ToString()
        };

        foreach (var (cat, (max, allocated, remaining)) in budget.GetSnapshot())
        {
            metadata[$"budget_{cat}_max"] = max.ToString();
            metadata[$"budget_{cat}_used"] = allocated.ToString();
        }

        return new StructuredContext
        {
            Query = retrievalResult.Query.Query,
            Intent = intent,
            Summary = summary,
            SemanticPaths = redPaths,
            Routes = routes,
            Entities = redEntities,
            Tables = redTables,
            BusinessRules = redRules,
            CompressedMethods = redMethods,
            TokenEstimate = tokenEstimate,
            Metadata = metadata
        };
    }

    private IReadOnlyList<string> BuildRoutes(
        List<RetrievalCandidate> routeChunks,
        TokenBudgetAllocator budget)
    {
        if (routeChunks.Count == 0) return Array.Empty<string>();

        var routeNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in routeChunks.Take(_options.MaxRoutes))
        {
            foreach (var nid in c.Chunk.NodeIds)
                routeNodeIds.Add(nid);
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nodeId in routeNodeIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            var node = _query.GetNode(nodeId);
            if (node is null) continue;

            var route = node.Attributes.GetValueOrDefault("route", "");
            var httpMethod = node.Attributes.GetValueOrDefault("http-method", "");
            var line = string.IsNullOrEmpty(route)
                ? $"Entry: {node.Label}"
                : $"[{httpMethod}] {route} → {node.Label}";

            if (!seen.Add(line)) continue;

            var tokens = ContextBudgetEstimator.Estimate(line);
            if (!budget.TryAllocate("routes", tokens)) break;

            result.Add(line);
        }

        return result;
    }

    private IReadOnlyList<string> BuildSemanticPaths(
        List<RetrievalCandidate> pathChunks,
        TokenBudgetAllocator budget)
    {
        if (pathChunks.Count == 0) return Array.Empty<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in pathChunks.Take(_options.MaxSemanticPaths))
        {
            var summary = c.Chunk.Summary;
            if (string.IsNullOrEmpty(summary)) continue;
            if (!seen.Add(summary)) continue;

            var tokens = ContextBudgetEstimator.Estimate(summary);
            if (!budget.TryAllocate("semantic_paths", tokens)) break;

            result.Add(summary);
        }

        return result;
    }

    private (IReadOnlyList<string> Entities, IReadOnlyList<string> Tables) BuildEntityTableMappings(
        List<RetrievalCandidate> entityChunks,
        TokenBudgetAllocator budget)
    {
        var entitySet = new HashSet<string>(StringComparer.Ordinal);
        var tableSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in entityChunks.Take(_options.MaxEntities))
        {
            foreach (var entity in c.Chunk.EntityNames)
            {
                if (entitySet.Count >= _options.MaxEntities) break;
                entitySet.Add(entity);
            }
            foreach (var table in c.Chunk.TableNames)
            {
                if (tableSet.Count >= _options.MaxTables) break;
                tableSet.Add(table);
            }
        }

        var entities = new List<string>();
        foreach (var e in entitySet.OrderBy(e => e, StringComparer.Ordinal))
        {
            var tokens = ContextBudgetEstimator.Estimate(e);
            if (budget.TryAllocate("metadata", tokens))
                entities.Add(e);
            else break;
        }

        var tables = new List<string>();
        foreach (var t in tableSet.OrderBy(t => t, StringComparer.Ordinal))
        {
            var tokens = ContextBudgetEstimator.Estimate(t);
            if (budget.TryAllocate("metadata", tokens))
                tables.Add(t);
            else break;
        }

        return (entities, tables);
    }

    private IReadOnlyList<string> BuildBusinessRules(
        List<CodeChunk> allChunks,
        TokenBudgetAllocator budget)
    {
        if (!_options.EnableBusinessRuleExtraction) return Array.Empty<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in allChunks)
        {
            var rules = BusinessRuleExtractor.ExtractRules(chunk.Content);
            foreach (var rule in rules)
            {
                if (result.Count >= _options.MaxBusinessRules) break;
                if (!seen.Add(rule)) continue;

                var tokens = ContextBudgetEstimator.Estimate(rule);
                if (!budget.TryAllocate("business_rules", tokens)) break;

                result.Add(rule);
            }

            if (result.Count >= _options.MaxBusinessRules) break;
        }

        return result;
    }

    private IReadOnlyList<string> BuildCompressedMethods(
        List<RetrievalCandidate> methodChunks,
        TokenBudgetAllocator budget)
    {
        if (methodChunks.Count == 0) return Array.Empty<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in methodChunks.OrderByDescending(c => c.FusedScore))
        {
            if (result.Count >= _options.MaxCompressedMethods) break;

            string content;
            if (_options.EnableSemanticCompression)
            {
                var cr = MethodCompressor.Compress(c.Chunk.Content, new[] { c.Chunk.ChunkId });
                content = cr.CompressedContent;
            }
            else
            {
                content = c.Chunk.Content;
            }

            if (string.IsNullOrEmpty(content)) continue;

            var title = c.Chunk.Title;
            var full = $"{title}\n{content}";
            if (!seen.Add(full)) continue;

            var tokens = ContextBudgetEstimator.Estimate(full);
            if (!budget.TryAllocate("methods", tokens))
            {
                var remaining = budget.GetRemaining("methods");
                if (remaining > 100)
                {
                    var truncated = TruncateContent(title, content, remaining);
                    if (budget.TryAllocate("methods", ContextBudgetEstimator.Estimate(truncated)))
                    {
                        result.Add(truncated);
                    }
                }
                break;
            }

            result.Add(full);
        }

        return result;
    }

    private static string TruncateContent(string title, string content, int maxTokens)
    {
        var lines = content.Split('\n');
        var header = title + "\n";
        var sb = new System.Text.StringBuilder(header);
        var currentTokens = ContextBudgetEstimator.Estimate(header);

        foreach (var line in lines)
        {
            var lineTokens = ContextBudgetEstimator.Estimate(line);
            if (currentTokens + lineTokens > maxTokens - 30) break;
            sb.AppendLine(line);
            currentTokens += lineTokens;
        }

        sb.AppendLine("// ... truncated for budget");
        return sb.ToString();
    }

    private static string InferIntent(string query, List<CodeChunk> chunks)
    {
        var queryLower = query.ToLowerInvariant();

        if (queryLower.Contains("api") || queryLower.Contains("route") || queryLower.Contains("endpoint"))
            return "API discovery";

        if (queryLower.Contains("table") || queryLower.Contains("database") || queryLower.Contains("schema"))
            return "Schema exploration";

        if (queryLower.Contains("entity") || queryLower.Contains("model") || queryLower.Contains("domain"))
            return "Domain model analysis";

        if (queryLower.Contains("flow") || queryLower.Contains("process") || queryLower.Contains("pipeline"))
            return "Process analysis";

        if (queryLower.Contains("impact") || queryLower.Contains("affect") || queryLower.Contains("dependency"))
            return "Impact analysis";

        if (queryLower.Contains("validate") || queryLower.Contains("check") || queryLower.Contains("rule"))
            return "Business rule verification";

        if (chunks.Any(c => c.Kind == ChunkKind.Route))
            return "Route-centric exploration";

        if (chunks.Any(c => c.Kind == ChunkKind.EntityAccess))
            return "Data access exploration";

        return "General code exploration";
    }

    private static string BuildSummary(
        string query,
        IReadOnlyList<RetrievalCandidate> candidates,
        IReadOnlyList<string> paths,
        IReadOnlyList<string> entities,
        IReadOnlyList<string> tables)
    {
        var parts = new List<string>();
        parts.Add($"Query: {query}");

        if (candidates.Count > 0)
            parts.Add($"Found {candidates.Count} relevant code contexts.");

        if (paths.Count > 0)
            parts.Add($"{paths.Count} semantic paths mapped.");

        if (entities.Count > 0 || tables.Count > 0)
            parts.Add($"Covers {entities.Count} entities across {tables.Count} tables.");

        if (candidates.Count > 0)
        {
            var top = candidates[0];
            parts.Add($"Top match: [{top.FusedScore:F2}] {top.Chunk.Title} ({top.Chunk.Kind})");
        }

        return string.Join(" ", parts);
    }
}

// =============================================================================
// SemanticDoc/RetrievalProfiles.cs — per-task retrieval weights (7A-3)
// =============================================================================
// Purpose: Different tasks need different retrieval weight profiles.
// Database queries need keyword boost; code modification needs embedding.
// =============================================================================

using Core.Graph;

namespace Core.Cognition.SemanticDoc;

public sealed class RetrievalProfile
{
    public required string ProfileName { get; init; }
    public double EmbeddingWeight { get; init; }
    public double GraphWeight { get; init; }
    public double KeywordWeight { get; init; }

    public static RetrievalProfile CodeModification { get; } = new()
    {
        ProfileName = "CodeModification",
        EmbeddingWeight = 0.6,
        GraphWeight = 0.3,
        KeywordWeight = 0.1,
    };

    public static RetrievalProfile BugAnalysis { get; } = new()
    {
        ProfileName = "BugAnalysis",
        EmbeddingWeight = 0.5,
        GraphWeight = 0.3,
        KeywordWeight = 0.2,
    };

    public static RetrievalProfile Architecture { get; } = new()
    {
        ProfileName = "Architecture",
        EmbeddingWeight = 0.3,
        GraphWeight = 0.6,
        KeywordWeight = 0.1,
    };

    public static RetrievalProfile Database { get; } = new()
    {
        ProfileName = "Database",
        EmbeddingWeight = 0.3,
        GraphWeight = 0.2,
        KeywordWeight = 0.5,
    };

    public static RetrievalProfile Default { get; } = CodeModification;

    public static RetrievalProfile ForQueryType(QueryType queryType) => queryType switch
    {
        QueryType.Database => new() { ProfileName = "Database", EmbeddingWeight = 0.2, GraphWeight = 0.1, KeywordWeight = 0.7 },
        QueryType.DTO => new() { ProfileName = "DTO", EmbeddingWeight = 0.4, GraphWeight = 0.2, KeywordWeight = 0.4 },
        QueryType.Exception => new() { ProfileName = "Exception", EmbeddingWeight = 0.3, GraphWeight = 0.2, KeywordWeight = 0.5 },
        QueryType.HTTP => new() { ProfileName = "HTTP", EmbeddingWeight = 0.3, GraphWeight = 0.3, KeywordWeight = 0.4 },
        QueryType.Architecture => new() { ProfileName = "Architecture", EmbeddingWeight = 0.3, GraphWeight = 0.6, KeywordWeight = 0.1 },
        QueryType.BugAnalysis => new() { ProfileName = "BugAnalysis", EmbeddingWeight = 0.5, GraphWeight = 0.4, KeywordWeight = 0.1 },
        QueryType.CodeModification => new() { ProfileName = "CodeModification", EmbeddingWeight = 0.6, GraphWeight = 0.3, KeywordWeight = 0.1 },
        QueryType.BusinessWorkflow => new() { ProfileName = "BusinessWorkflow", EmbeddingWeight = 0.4, GraphWeight = 0.4, KeywordWeight = 0.2 },
        _ => Default,
    };
}

public enum SearchMode { Hybrid = 0, EmbeddingOnly = 1, GraphOnly = 2, KeywordOnly = 3 }

public sealed class HybridRetrievalService
{
    private readonly SemanticEmbeddingService _embeddingService;
    private readonly GraphQueryService _graphQuery;

    public HybridRetrievalService(SemanticEmbeddingService embeddingService, GraphQueryService graphQuery)
    {
        _embeddingService = embeddingService;
        _graphQuery = graphQuery;
    }

    public FilterResult Search(
        string query,
        ReverseIndex reverseIndex,
        RetrievalProfile? profile = null,
        SearchMode mode = SearchMode.Hybrid,
        QueryType? queryType = null)
    {
        var p = profile ?? (queryType is not null ? RetrievalProfile.ForQueryType(queryType.Value) : RetrievalProfile.Default);
        var results = new Dictionary<string, ScoredMethodResult>(StringComparer.Ordinal);

        if (mode is SearchMode.Hybrid or SearchMode.EmbeddingOnly)
        {
            var vecResults = _embeddingService.Search(query, 20);
            foreach (var r in vecResults)
            {
                if (!results.TryGetValue(r.ChunkId, out var sr))
                { sr = NewResult(r.ChunkId); results[r.ChunkId] = sr; }
                sr.EmbeddingScore = r.Similarity;
            }
        }

        if (mode is SearchMode.Hybrid or SearchMode.GraphOnly)
        {
            var graphCandidates = GraphKeywordSearch(query);
            foreach (var (id, score) in graphCandidates)
            {
                if (!results.TryGetValue(id, out var sr))
                { sr = NewResult(id); results[id] = sr; }
                sr.GraphScore = score;
            }
        }

        if (mode is SearchMode.Hybrid or SearchMode.KeywordOnly)
        {
            var keywordHits = ReverseIndexSearch(query, reverseIndex);
            foreach (var (id, score) in keywordHits)
            {
                if (!results.TryGetValue(id, out var sr))
                { sr = NewResult(id); results[id] = sr; }
                sr.KeywordScore = score;
            }
        }

        // Composite score
        foreach (var r in results.Values)
        {
            r.CompositeScore = mode switch
            {
                SearchMode.EmbeddingOnly => r.EmbeddingScore,
                SearchMode.GraphOnly => r.GraphScore,
                SearchMode.KeywordOnly => r.KeywordScore,
                _ => r.EmbeddingScore * p.EmbeddingWeight
                   + r.GraphScore * p.GraphWeight
                   + r.KeywordScore * p.KeywordWeight,
            };
            // Enrich with node metadata
            var node = _graphQuery.GetNode(r.MethodId);
            r.MethodName = node?.MethodName ?? "";
            r.ClassName = node?.ClassName ?? "";
            r.SourceFile = node?.SourceFile ?? "";
        }

        // Apply noise filter with RankingRuleSet (produces traces)
        return NoiseTermFilter.Apply(
            results.Values.OrderByDescending(r => r.CompositeScore).ToList(),
            query, p, mode);
    }

    private ScoredMethodResult NewResult(string id) => new() { MethodId = id };

    private Dictionary<string, double> GraphKeywordSearch(string query)
    {
        var results = new Dictionary<string, double>(StringComparer.Ordinal);
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var node in _graphQuery.GetAllNodes().Take(500))
        {
            // Skip base/framework classes (Step 4)
            if (IsBaseOrFramework(node)) continue;

            double score = 0;
            foreach (var w in words)
            {
                if (string.Equals(node.MethodName, w, StringComparison.OrdinalIgnoreCase)) score += 1.0;
                else if (node.MethodName.Contains(w, StringComparison.OrdinalIgnoreCase)) score += 0.6;
                else if (node.ClassName?.Contains(w, StringComparison.OrdinalIgnoreCase) == true) score += 0.4;
            }
            if (score > 0)
            {
                // Same-class bonus
                if (node.ClassName is not null && words.Any(w =>
                    node.ClassName.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    score += 0.15;

                results[node.Id] = Math.Min(score / words.Length, 1.0);
            }
        }
        return results;
    }

    private static bool IsBaseOrFramework(Graph.GraphNode node)
    {
        var cls = node.ClassName ?? "";
        // Skip base/generic infrastructure classes
        if (cls.StartsWith("Base", StringComparison.Ordinal)
            && (cls.Contains("BLL", StringComparison.Ordinal)
             || cls.Contains("DAO", StringComparison.Ordinal)
             || cls.Contains("Dao", StringComparison.Ordinal)
             || cls.Contains("NHB", StringComparison.Ordinal)))
            return true;
        if (cls == "HibernateDaoSupport") return true;
        return false;
    }

    private static Dictionary<string, double> ReverseIndexSearch(string query, ReverseIndex index)
    {
        var results = new Dictionary<string, double>(StringComparer.Ordinal);
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            SearchIndex(word, index.TableToMethods, results, 1.0);
            SearchIndex(word, index.ExceptionToMethods, results, 0.8);
            SearchIndex(word, index.ConfigKeyToMethods, results, 0.5);
            SearchIndex(word, index.HttpUrlToMethods, results, 0.6);
        }
        return results;
    }

    private static void SearchIndex(
        string query,
        Dictionary<string, List<ReverseIndexEntry>> index,
        Dictionary<string, double> results,
        double weight)
    {
        foreach (var (key, entries) in index)
        {
            if (key.Contains(query, StringComparison.OrdinalIgnoreCase)
                || query.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in entries)
                {
                    var current = results.GetValueOrDefault(entry.MethodId, 0);
                    results[entry.MethodId] = Math.Max(current, weight);
                }
            }
        }
    }
}

public sealed class ScoredMethodResult
{
    public required string MethodId { get; init; }
    public string MethodName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public double EmbeddingScore { get; set; }
    public double GraphScore { get; set; }
    public double KeywordScore { get; set; }
    public double CompositeScore { get; set; }

    public string DisplayLabel =>
        string.IsNullOrEmpty(MethodName) ? MethodId : $"{ClassName}.{MethodName}";
}

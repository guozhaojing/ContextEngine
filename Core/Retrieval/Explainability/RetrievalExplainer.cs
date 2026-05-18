using Core.Retrieval.Retrieval;

namespace Core.Retrieval.Explainability;

public static class RetrievalExplainer
{
    public static RetrievalExplanation Explain(
        RetrievalCandidate candidate,
        RetrievalQuery query)
    {
        var chunk = candidate.Chunk;
        var meta = chunk.Metadata;

        var queryTokens = Tokenize(query.Query);
        var matchedKeywords = chunk.Keywords
            .Where(k => queryTokens.Any(qt => k.Contains(qt, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var summary = BuildSummary(candidate, meta);

        return new RetrievalExplanation
        {
            ChunkId = chunk.ChunkId,
            ChunkTitle = chunk.Title,
            Scores = new ScoreBreakdown
            {
                VectorSimilarity = Math.Round(candidate.VectorSimilarity, 4),
                GraphRelevance = Math.Round(candidate.GraphRelevance, 4),
                BusinessRelevance = Math.Round(candidate.BusinessRelevance, 4),
                ImportanceScore = Math.Round(chunk.ImportanceScore, 2),
                FusedScore = Math.Round(candidate.FusedScore, 4)
            },
            MatchedKeywords = matchedKeywords,
            SharedEntities = chunk.EntityNames ?? Array.Empty<string>(),
            SharedTables = chunk.TableNames ?? Array.Empty<string>(),
            SharedRoutes = chunk.RoutePatterns ?? Array.Empty<string>(),
            EntryPointDistance = meta?.EntryPointDistance ?? -1,
            DataAccessDistance = meta?.DataAccessDistance ?? -1,
            IsEntryPoint = meta?.IsEntryPoint ?? false,
            IsEntityAccess = meta?.IsEntityAccess ?? false,
            Summary = summary
        };
    }

    public static IReadOnlyList<RetrievalExplanation> ExplainAll(
        RetrievalResult result,
        RetrievalQuery query,
        int topN = 5)
    {
        return result.Candidates
            .Take(topN)
            .Select(c => Explain(c, query))
            .ToList();
    }

    private static string BuildSummary(RetrievalCandidate candidate, Ranking.ChunkMetadata? meta)
    {
        var parts = new List<string>();

        if (candidate.VectorSimilarity > 0.7)
            parts.Add("high vector similarity");
        else if (candidate.VectorSimilarity < 0.3)
            parts.Add("low vector match");

        if (meta is null) return string.Join(", ", parts);

        if (meta.IsEntryPoint)
            parts.Add("API entry point");

        if (meta.IsEntityAccess)
            parts.Add("entity access");

        if (meta.EntryPointDistance >= 0 && meta.EntryPointDistance <= 2)
            parts.Add($"near API ({meta.EntryPointDistance}h)");

        if (meta.DataAccessDistance >= 0 && meta.DataAccessDistance <= 2)
            parts.Add($"near data ({meta.DataAccessDistance}h)");

        if (meta.FanIn + meta.FanOut > 20)
            parts.Add("high connectivity");

        if (meta.IsCrossProject)
            parts.Add("cross-project");

        return string.Join(", ", parts);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in text.Split(' ', '\n', '\t', '.', ',', ';'))
        {
            var trimmed = word.Trim();
            if (trimmed.Length > 1)
                tokens.Add(trimmed);
        }
        return tokens;
    }
}

// =============================================================================
// QueryUnderstanding/QueryExplanation.cs — 检索解释
// =============================================================================
// 输出为什么命中：
//   - matched entity
//   - matched table
//   - matched route
//   - matched keyword
//   - 置信度评分
// =============================================================================

namespace Core.QueryUnderstanding;

public sealed class QueryExplanation
{
    public string Query { get; set; } = "";

    public QueryIntent Intent { get; set; }

    public List<MatchExplanation> MatchedEntities { get; set; } = new();

    public List<MatchExplanation> MatchedTables { get; set; } = new();

    public List<MatchExplanation> MatchedRoutes { get; set; } = new();

    public List<MatchExplanation> MatchedKeywords { get; set; } = new();

    public List<MatchExplanation> MatchedClasses { get; set; } = new();

    public List<MatchExplanation> MatchedMethods { get; set; } = new();

    public double TotalConfidence =>
        AllMatches.Count == 0
            ? 0
            : AllMatches.Average(m => m.Confidence);

    public int MatchCount => AllMatches.Count;

    private List<MatchExplanation> AllMatches =>
        MatchedEntities.Concat(MatchedTables).Concat(MatchedRoutes)
            .Concat(MatchedKeywords).Concat(MatchedClasses).Concat(MatchedMethods)
            .ToList();

    public static QueryExplanation Empty => new()
    {
        Query = "",
        Intent = QueryIntent.Unknown
    };
}

public sealed class MatchExplanation
{
    public string MatchedTerm { get; set; } = "";

    public string Category { get; set; } = "";

    public string Source { get; set; } = "";

    public double Confidence { get; set; }

    public string? OriginalName { get; set; }

    public string? FilePath { get; set; }
}

public static class QueryExplanationBuilder
{
    public static QueryExplanation Build(
        string query,
        QueryExpansionResult expansion,
        RewrittenQuery rewritten,
        QueryIntent intent,
        ProjectVocabulary vocabulary)
    {
        var explanation = new QueryExplanation
        {
            Query = query,
            Intent = intent,
            MatchedEntities = new List<MatchExplanation>(),
            MatchedTables = new List<MatchExplanation>(),
            MatchedRoutes = new List<MatchExplanation>(),
            MatchedKeywords = new List<MatchExplanation>(),
            MatchedClasses = new List<MatchExplanation>(),
            MatchedMethods = new List<MatchExplanation>()
        };

        // 从扩展结果中提取匹配信息
        foreach (var (token, candidates) in expansion.TokenExpansions)
        {
            foreach (var candidate in candidates.Where(c => c.Score >= 0.5))
            {
                var match = new MatchExplanation
                {
                    MatchedTerm = candidate.Term,
                    Source = candidate.Source,
                    Confidence = candidate.Score
                };

                // 查找对应的 VocabularyEntry
                var entry = FindEntry(vocabulary, candidate);
                if (entry is not null)
                {
                    match.OriginalName = entry.Original;
                    match.FilePath = entry.FilePath;
                }

                switch (candidate.Kind?.ToLowerInvariant())
                {
                    case "entity":
                        explanation.MatchedEntities.Add(match);
                        break;
                    case "table":
                        explanation.MatchedTables.Add(match);
                        break;
                    case "route":
                        explanation.MatchedRoutes.Add(match);
                        break;
                    case "controller":
                    case "class":
                        explanation.MatchedClasses.Add(match);
                        break;
                    case "method":
                        explanation.MatchedMethods.Add(match);
                        break;
                    default:
                        explanation.MatchedKeywords.Add(match);
                        break;
                }
            }
        }

        // 复合匹配
        foreach (var compound in expansion.CompoundExpansions.Where(c => c.Score >= 0.5))
        {
            var match = new MatchExplanation
            {
                MatchedTerm = compound.Term,
                Source = compound.Source,
                Confidence = compound.Score
            };

            switch (compound.Kind?.ToLowerInvariant())
            {
                case "entity":
                    explanation.MatchedEntities.Add(match);
                    break;
                case "table":
                    explanation.MatchedTables.Add(match);
                    break;
                case "route":
                    explanation.MatchedRoutes.Add(match);
                    break;
                default:
                    explanation.MatchedKeywords.Add(match);
                    break;
            }
        }

        // 去重（按 MatchedTerm + Category）
        Deduplicate(explanation.MatchedEntities, "entity");
        Deduplicate(explanation.MatchedTables, "table");
        Deduplicate(explanation.MatchedRoutes, "route");
        Deduplicate(explanation.MatchedClasses, "class");
        Deduplicate(explanation.MatchedMethods, "method");
        Deduplicate(explanation.MatchedKeywords, "keyword");

        return explanation;
    }

    private static VocabularyEntry? FindEntry(ProjectVocabulary vocabulary, ExpansionCandidate candidate)
    {
        foreach (var entry in vocabulary.Entities)
            if (string.Equals(entry.Original, candidate.Term, StringComparison.Ordinal))
                return entry;
        foreach (var entry in vocabulary.Tables)
            if (string.Equals(entry.Original, candidate.Term, StringComparison.Ordinal))
                return entry;
        foreach (var entry in vocabulary.Routes)
            if (string.Equals(entry.Original, candidate.Term, StringComparison.Ordinal))
                return entry;
        foreach (var entry in vocabulary.Classes)
            if (string.Equals(entry.Original, candidate.Term, StringComparison.Ordinal))
                return entry;
        foreach (var entry in vocabulary.Methods)
            if (string.Equals(entry.Original, candidate.Term, StringComparison.Ordinal))
                return entry;

        return null;
    }

    private static void Deduplicate(List<MatchExplanation> matches, string category)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<MatchExplanation>();

        foreach (var match in matches)
        {
            var key = $"{match.MatchedTerm}|{category}";
            if (seen.Add(key))
            {
                match.Category = category;
                deduped.Add(match);
            }
        }

        matches.Clear();
        matches.AddRange(deduped);
    }
}

// =============================================================================
// SemanticDoc/NoiseTermFilter.cs — uses RankingRuleSet, produces RetrievalTrace
// =============================================================================
using System.Collections.Frozen;

namespace Core.Cognition.SemanticDoc;

public static class NoiseTermFilter
{
    public static FilterResult Apply(
        List<ScoredMethodResult> results,
        string query,
        RetrievalProfile profile,
        SearchMode mode,
        int keepTop = 10)
    {
        if (results.Count == 0)
            return new FilterResult { Filtered = results, Traces = Array.Empty<RetrievalTrace>() };

        var ruleSet = RankingRuleSet.Default;
        var queryWords = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)
            .ToFrozenSet(StringComparer.Ordinal);

        var traces = new List<RetrievalTrace>();

        foreach (var r in results)
        {
            var bonuses = new List<RankingEffect>();
            var penalties = new List<RankingEffect>();
            var ctx = new RuleContext
            {
                MethodId = r.MethodId, MethodName = r.MethodName,
                ClassName = r.ClassName, SourceFile = r.SourceFile,
                QueryWords = queryWords,
            };

            var effects = ruleSet.Apply(ctx);
            foreach (var eff in effects)
            {
                if (eff.Value <= 0) continue;
                if (eff.Reason.StartsWith("Noise/", StringComparison.Ordinal))
                    penalties.Add(eff);
                else
                    bonuses.Add(eff);
            }

            var bonusSum = bonuses.Sum(b => b.Value);
            var penaltySum = penalties.Sum(p => p.Value);
            r.CompositeScore *= (1.0 - Math.Min(penaltySum, 0.7));

            traces.Add(new RetrievalTrace
            {
                MethodId = r.MethodId,
                MethodName = r.MethodName,
                ClassName = r.ClassName,
                FinalScore = r.CompositeScore,
                EmbeddingScore = r.EmbeddingScore,
                GraphScore = r.GraphScore,
                KeywordScore = r.KeywordScore,
                Bonuses = bonuses,
                Penalties = penalties,
                RetrievalProfile = profile.ProfileName,
                Mode = mode,
            });
        }

        var filtered = results
            .OrderByDescending(r => r.CompositeScore)
            .Take(keepTop)
            .ToList();

        return new FilterResult { Filtered = filtered, Traces = traces };
    }
}

public sealed class FilterResult
{
    public required IReadOnlyList<ScoredMethodResult> Filtered { get; init; }
    public required IReadOnlyList<RetrievalTrace> Traces { get; init; }
}

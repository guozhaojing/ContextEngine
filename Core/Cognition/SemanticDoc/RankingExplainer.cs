// =============================================================================
// SemanticDoc/RankingExplainer.cs — traceability + centralized rules + noise report
// =============================================================================
// Purpose: Make every ranking decision explainable. No scattered if/else rules.
// =============================================================================

namespace Core.Cognition.SemanticDoc;

// ═══════════════════════════════════════════════════════════════
// RetrievalTrace — per-result traceability
// ═══════════════════════════════════════════════════════════════

public sealed class RetrievalTrace
{
    public required string MethodId { get; init; }
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }

    public double FinalScore { get; set; }
    public double EmbeddingScore { get; set; }
    public double GraphScore { get; set; }
    public double KeywordScore { get; set; }

    public required IReadOnlyList<RankingEffect> Bonuses { get; set; }
    public required IReadOnlyList<RankingEffect> Penalties { get; set; }
    public string RetrievalProfile { get; set; } = "";
    public SearchMode Mode { get; set; }

    public string Explain()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{ClassName}.{MethodName}");
        sb.AppendLine($"FinalScore: {FinalScore:F3} (embed={EmbeddingScore:F3} graph={GraphScore:F3} keyword={KeywordScore:F3} profile={RetrievalProfile})");
        if (Bonuses.Count > 0)
        {
            sb.AppendLine("Bonuses:");
            foreach (var b in Bonuses) sb.AppendLine($"  + {b.Reason} (+{b.Value:F2})");
        }
        if (Penalties.Count > 0)
        {
            sb.AppendLine("Penalties:");
            foreach (var p in Penalties) sb.AppendLine($"  - {p.Reason} (-{p.Value:F2})");
        }
        return sb.ToString();
    }
}

public sealed class RankingEffect
{
    public required string Reason { get; init; }
    public required double Value { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// RankingRuleSet — centralized bonus/penalty (no scattered if/else)
// ═══════════════════════════════════════════════════════════════

public sealed class RankingRuleSet
{
    public List<RankingRule> Rules { get; } = new();

    public static RankingRuleSet Default
    {
        get
        {
            var set = new RankingRuleSet();

            // Graph bonuses
            set.Rules.Add(new RankingRule
            {
                Name = "SameClassMatch", Category = "Graph", Effect = RuleEffect.Bonus,
                Value = 0.15, Condition = (ctx, q) =>
                    ctx.ClassName.Length > 0 && ctx.QueryWords.Any(w =>
                        ctx.ClassName.StartsWith(w, StringComparison.OrdinalIgnoreCase)),
            });

            // CRUD penalties
            var crudMethods = new HashSet<string>(StringComparer.Ordinal)
                { "Save", "Update", "Delete", "Remove", "Get", "Load", "LoadAll", "Flush", "Evict", "Find" };
            set.Rules.Add(new RankingRule
            {
                Name = "GenericCRUD", Category = "Noise", Effect = RuleEffect.Penalty,
                Value = 0.3, Condition = (ctx, q) =>
                    crudMethods.Contains(ctx.MethodName) && !ctx.QueryWords.Any(w =>
                        ctx.MethodName.Equals(w, StringComparison.OrdinalIgnoreCase)),
            });

            // Base class penalty
            set.Rules.Add(new RankingRule
            {
                Name = "BaseClassNoise", Category = "Noise", Effect = RuleEffect.Penalty,
                Value = 0.4, Condition = (ctx, q) =>
                    ctx.ClassName.StartsWith("Base", StringComparison.Ordinal)
                    && (ctx.ClassName.Contains("BLL", StringComparison.Ordinal)
                     || ctx.ClassName.Contains("DAO", StringComparison.Ordinal)
                     || ctx.ClassName.Contains("Dao", StringComparison.Ordinal)
                     || ctx.ClassName.Contains("NHB", StringComparison.Ordinal)),
            });

            // Framework utility penalty
            var fwNs = new HashSet<string>(StringComparer.Ordinal)
                { "ZhiFang.Common.Log", "ZhiFang.Common.Public", "ZhiFang.DAO.NHB.Base",
                  "ZhiFang.BLL.Base", "ZhiFang.IBLL.Base", "ZhiFang.IDAO.Base" };
            set.Rules.Add(new RankingRule
            {
                Name = "FrameworkUtility", Category = "Noise", Effect = RuleEffect.Penalty,
                Value = 0.25, Condition = (ctx, q) =>
                    fwNs.Any(ns => ctx.SourceFile.Contains(ns.Replace('.', '\\'), StringComparison.OrdinalIgnoreCase)
                                || ctx.SourceFile.Contains(ns.Replace('.', '/'), StringComparison.OrdinalIgnoreCase)),
            });

            // Low-context method penalty
            set.Rules.Add(new RankingRule
            {
                Name = "LowBusinessContext", Category = "Noise", Effect = RuleEffect.Penalty,
                Value = 0.15, Condition = (ctx, q) =>
                    ctx.MethodName.Length <= 5 || IsAllLowerOrDigits(ctx.MethodName),
            });

            return set;
        }
    }

    public List<RankingEffect> Apply(RuleContext ctx)
    {
        var effects = new List<RankingEffect>();
        foreach (var rule in Rules)
        {
            if (rule.Condition(ctx, ""))
            {
                effects.Add(new RankingEffect
                {
                    Reason = $"{rule.Category}/{rule.Name}", Value = rule.Value,
                });
            }
        }
        return effects;
    }

    private static bool IsAllLowerOrDigits(string s) =>
        s.Length > 0 && s.All(c => char.IsLower(c) || char.IsDigit(c));
}

public sealed class RankingRule
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public RuleEffect Effect { get; init; }
    public double Value { get; init; }
    public required Func<RuleContext, string, bool> Condition { get; init; }
}

public enum RuleEffect { Bonus = 0, Penalty = 1 }

public sealed class RuleContext
{
    public required string MethodId { get; init; }
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
    public required string SourceFile { get; init; }
    public required IReadOnlySet<string> QueryWords { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// NoiseContributionReport — identify pollution sources
// ═══════════════════════════════════════════════════════════════

public sealed class NoiseContributionReport
{
    public Dictionary<string, int> MethodNoiseCounts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ClassNoiseCounts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> RuleTriggerCounts { get; } = new(StringComparer.Ordinal);

    public static NoiseContributionReport FromTraces(IReadOnlyList<RetrievalTrace> traces)
    {
        var report = new NoiseContributionReport();
        foreach (var t in traces)
        {
            foreach (var p in t.Penalties)
            {
                report.RuleTriggerCounts[p.Reason] = report.RuleTriggerCounts.GetValueOrDefault(p.Reason, 0) + 1;
            }
            if (t.Penalties.Count > 0)
            {
                report.MethodNoiseCounts[t.MethodName] = report.MethodNoiseCounts.GetValueOrDefault(t.MethodName, 0) + 1;
                report.ClassNoiseCounts[t.ClassName] = report.ClassNoiseCounts.GetValueOrDefault(t.ClassName, 0) + 1;
            }
        }
        return report;
    }

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Noise Contribution Report");
        sb.AppendLine();

        sb.AppendLine("## Top Noise Methods (most penalized)");
        foreach (var (name, count) in MethodNoiseCounts.OrderByDescending(kvp => kvp.Value).Take(8))
            sb.AppendLine($"- {name}: {count}");

        sb.AppendLine();
        sb.AppendLine("## Top Noise Classes (most penalized)");
        foreach (var (cls, count) in ClassNoiseCounts.OrderByDescending(kvp => kvp.Value).Take(8))
            sb.AppendLine($"- {cls}: {count}");

        sb.AppendLine();
        sb.AppendLine("## Rule Trigger Distribution");
        foreach (var (rule, count) in RuleTriggerCounts.OrderByDescending(kvp => kvp.Value))
            sb.AppendLine($"- {rule}: {count}");

        return sb.ToString();
    }
}

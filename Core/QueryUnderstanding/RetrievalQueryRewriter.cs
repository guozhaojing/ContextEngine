// =============================================================================
// QueryUnderstanding/RetrievalQueryRewriter.cs — 检索查询重写器
// =============================================================================
// 输入: "试剂流程"
// 输出:
//   expanded query: "reagent flow entity access repository"
//   tokens: [reagent, EQA_Reagent, flow, entity, access, repository]
// 策略:
//   1. 同义词替换 + 词汇扩展
//   2. 意图相关术语追加
//   3. 前缀匹配降噪
// =============================================================================

namespace Core.QueryUnderstanding;

public sealed class RetrievalQueryRewriter
{
    private readonly ProjectVocabulary _vocabulary;
    private readonly AliasGraph _aliasGraph;

    private static readonly Dictionary<QueryIntent, string[]> IntentKeywordSets = new()
    {
        [QueryIntent.FlowAnalysis] = new[]
            { "flow", "call", "method", "trace", "chain", "path" },
        [QueryIntent.ImpactAnalysis] = new[]
            { "impact", "inbound", "dependency", "affect", "change" },
        [QueryIntent.EntityLookup] = new[]
            { "entity", "table", "repository", "access", "dao", "data" },
        [QueryIntent.RouteLookup] = new[]
            { "api", "route", "controller", "endpoint", "http", "request" },
        [QueryIntent.ValidationLookup] = new[]
            { "validate", "rule", "business", "logic", "constraint", "check" },
        [QueryIntent.Unknown] = new[]
            { "entity", "table", "route", "method", "class" }
    };

    public RetrievalQueryRewriter(ProjectVocabulary vocabulary, AliasGraph aliasGraph)
    {
        _vocabulary = vocabulary;
        _aliasGraph = aliasGraph;
    }

    /// <summary>重写查询，返回扩展版。</summary>
    public RewrittenQuery Rewrite(string query, QueryExpansionResult expansion)
    {
        var intent = QueryIntentClassifier.Classify(query);
        var expandedTokens = new List<string>();
        var usedSources = new List<string>();

        // ① 从扩展结果中收集高置信度候选
        foreach (var (token, candidates) in expansion.TokenExpansions)
        {
            // 选取 Top-3 候选（Score ≥ 0.6）
            var topCandidates = candidates
                .Where(c => c.Score >= 0.6)
                .OrderByDescending(c => c.Score)
                .Take(3)
                .ToList();

            foreach (var candidate in topCandidates)
            {
                if (!expandedTokens.Contains(candidate.Term, StringComparer.OrdinalIgnoreCase))
                {
                    expandedTokens.Add(candidate.Term);
                    usedSources.Add($"expanded:{candidate.Source}({candidate.Score:F2})");
                }
            }
        }

        // ② 从复合扩展收集
        foreach (var compound in expansion.CompoundExpansions
            .Where(c => c.Score >= 0.7)
            .OrderByDescending(c => c.Score)
            .Take(3))
        {
            if (!expandedTokens.Contains(compound.Term, StringComparer.OrdinalIgnoreCase))
            {
                expandedTokens.Add(compound.Term);
                usedSources.Add($"compound:{compound.Source}({compound.Score:F2})");
            }
        }

        // ③ 追加意图关键词
        if (IntentKeywordSets.TryGetValue(intent, out var intentKeywords))
        {
            foreach (var keyword in intentKeywords)
            {
                if (!expandedTokens.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                {
                    expandedTokens.Add(keyword);
                    usedSources.Add($"intent:{intent}");
                }
            }
        }

        // ④ 构建重写后查询字符串
        var queryStr = string.Join(" ", expandedTokens);

        // ⑤ 生成检索用 token 列表（小写 + 原始）
        var retrievalTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in expandedTokens)
        {
            retrievalTokens.Add(t);
            retrievalTokens.Add(t.ToLowerInvariant());

            // 也加入 normalized 形式
            var normalized = new QueryNormalizer().NormalizeIdentifier(t);
            if (!string.IsNullOrEmpty(normalized) && normalized != t)
                retrievalTokens.Add(normalized);
        }

        return new RewrittenQuery
        {
            ExpandedQuery = queryStr,
            Tokens = retrievalTokens.ToList(),
            Intent = intent,
            Sources = usedSources
        };
    }
}

public sealed class RewrittenQuery
{
    public string ExpandedQuery { get; set; } = "";

    public List<string> Tokens { get; set; } = new();

    public QueryIntent Intent { get; set; }

    public List<string> Sources { get; set; } = new();
}

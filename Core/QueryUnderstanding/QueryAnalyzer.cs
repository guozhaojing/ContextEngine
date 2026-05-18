// =============================================================================
// QueryUnderstanding/QueryAnalyzer.cs — 主控制器
// =============================================================================
// 协调 QueryUnderstanding 各模块，提供单入口 Analyze API。
// 输出统一分析结果，包含：
//   - 意图分类
//   - 查询扩展
//   - 重写查询
//   - 解释信息
// =============================================================================

using System.Text.Json;

namespace Core.QueryUnderstanding;

public sealed class QueryAnalyzer
{
    private readonly ProjectVocabulary _vocabulary;
    private readonly AliasGraph _aliasGraph;
    private readonly QueryExpansion _expander = new();
    private readonly RetrievalQueryRewriter _rewriter;

    public QueryAnalyzer(ProjectVocabulary vocabulary)
    {
        _vocabulary = vocabulary ?? throw new ArgumentNullException(nameof(vocabulary));
        _aliasGraph = AliasGraph.FromVocabulary(vocabulary);
        _rewriter = new RetrievalQueryRewriter(vocabulary, _aliasGraph);
    }

    public QueryAnalyzer(ProjectVocabulary vocabulary, AliasGraph aliasGraph)
    {
        _vocabulary = vocabulary ?? throw new ArgumentNullException(nameof(vocabulary));
        _aliasGraph = aliasGraph ?? throw new ArgumentNullException(nameof(aliasGraph));
        _rewriter = new RetrievalQueryRewriter(vocabulary, _aliasGraph);
    }

    public QueryAnalysisResult Analyze(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return QueryAnalysisResult.Empty;

        var intent = QueryIntentClassifier.Classify(query);
        var expansion = _expander.Expand(query, _vocabulary, _aliasGraph);
        var rewritten = _rewriter.Rewrite(query, expansion);
        var explanation = QueryExplanationBuilder.Build(query, expansion, rewritten, intent, _vocabulary);

        return new QueryAnalysisResult
        {
            OriginalQuery = query,
            Intent = intent,
            Expansion = expansion,
            RewrittenQuery = rewritten,
            Explanation = explanation
        };
    }

    /// <summary>导出词汇表为 JSON。</summary>
    public string ExportVocabularyJson()
    {
        return JsonSerializer.Serialize(_vocabulary, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>导出重写 trace 为 JSON。</summary>
    public string ExportRewriteTrace(QueryAnalysisResult result)
    {
        var trace = new
        {
            result.OriginalQuery,
            Intent = result.Intent.ToString(),
            ExpandedTokens = result.Expansion.TokenExpansions
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(c => new { c.Term, c.Source, c.Score, c.Kind }),
                    StringComparer.Ordinal),
            CompoundExpansions = result.Expansion.CompoundExpansions
                .Select(c => new { c.Term, c.Source, c.Score, c.Kind }),
            RewrittenQuery = result.RewrittenQuery.ExpandedQuery,
            RewrittenTokens = result.RewrittenQuery.Tokens
        };

        return JsonSerializer.Serialize(trace, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

/// <summary>查询分析统一结果。</summary>
public sealed class QueryAnalysisResult
{
    public string OriginalQuery { get; set; } = "";

    public QueryIntent Intent { get; set; }

    public QueryExpansionResult Expansion { get; set; } = new();

    public RewrittenQuery RewrittenQuery { get; set; } = new();

    public QueryExplanation Explanation { get; set; } = QueryExplanation.Empty;

    public static QueryAnalysisResult Empty => new()
    {
        OriginalQuery = "",
        Intent = QueryIntent.Unknown
    };
}

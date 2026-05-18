// =============================================================================
// QueryUnderstanding/QueryIntent.cs — 查询意图分类（rule-based）
// =============================================================================
// 【边界】纯规则引擎；不接入 LLM；deterministic + explainable。
// 优先级：关键词 > 结构模式 > 回退默认。
// =============================================================================

namespace Core.QueryUnderstanding;

public enum QueryIntent
{
    Unknown,
    FlowAnalysis,
    ImpactAnalysis,
    EntityLookup,
    RouteLookup,
    ValidationLookup
}

public static class QueryIntentClassifier
{
    private static readonly HashSet<string> FlowKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "flow", "流程", "call", "调用", "chain", "链", "path", "路径",
        "trace", "追踪", "pipeline", "管线", "sequence", "顺序"
    };

    private static readonly HashSet<string> ImpactKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "impact", "影响", "affect", "affected", "波及", "change", "变更",
        "modify", "修改", "break", "破坏", "depend", "依赖", "risk", "风险"
    };

    private static readonly HashSet<string> EntityKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "entity", "实体", "table", "表", "data", "数据", "model", "模型",
        "repository", "dao", "access", "访问", "read", "write", "crud",
        "查询", "插入", "更新", "删除", "EQA", "equip", "reagent", "lab"
    };

    private static readonly HashSet<string> RouteKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "route", "路由", "api", "接口", "endpoint", "端点", "controller",
        "action", "url", "path", "http", "request", "请求", "response", "响应"
    };

    private static readonly HashSet<string> ValidationKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "validate", "校验", "verify", "验证", "check", "检查", "rule", "规则",
        "business", "业务", "logic", "逻辑", "constraint", "约束", "condition", "条件",
        "approve", "审批", "audit", "审计", "review", "复审"
    };

    public static QueryIntent Classify(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return QueryIntent.Unknown;

        var tokens = Tokenize(query);
        var scores = new Dictionary<QueryIntent, int>
        {
            [QueryIntent.FlowAnalysis] = CountMatches(tokens, FlowKeywords),
            [QueryIntent.ImpactAnalysis] = CountMatches(tokens, ImpactKeywords),
            [QueryIntent.EntityLookup] = CountMatches(tokens, EntityKeywords),
            [QueryIntent.RouteLookup] = CountMatches(tokens, RouteKeywords),
            [QueryIntent.ValidationLookup] = CountMatches(tokens, ValidationKeywords)
        };

        var best = scores.OrderByDescending(kv => kv.Value).First();
        if (best.Value == 0)
            return QueryIntent.Unknown;

        // 如果出现平局，按优先级排序
        var maxScore = best.Value;
        var candidates = scores.Where(kv => kv.Value == maxScore).ToList();
        if (candidates.Count == 1)
            return candidates[0].Key;

        // 优先级: Entity > Route > Flow > Impact > Validation
        var priorityOrder = new[]
        {
            QueryIntent.EntityLookup,
            QueryIntent.RouteLookup,
            QueryIntent.FlowAnalysis,
            QueryIntent.ImpactAnalysis,
            QueryIntent.ValidationLookup
        };

        return priorityOrder.First(p => scores[p] == maxScore);
    }

    private static int CountMatches(string[] tokens, HashSet<string> keywords)
    {
        var count = 0;
        foreach (var token in tokens)
        {
            if (keywords.Contains(token))
                count++;
        }
        return count;
    }

    internal static string[] Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new List<char>();

        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                current.Add(char.ToLowerInvariant(c));
            }
            else
            {
                if (current.Count > 0)
                {
                    tokens.Add(new string(current.ToArray()));
                    current.Clear();
                }
            }
        }

        if (current.Count > 0)
            tokens.Add(new string(current.ToArray()));

        // 中文按字粒度作为额外 token（简单 n-gram）
        var chineseTokens = new List<string>();
        foreach (var t in tokens)
        {
            if (t.Length >= 2)
            {
                for (var i = 0; i <= t.Length - 2; i++)
                    chineseTokens.Add(t.Substring(i, 2));
            }
        }

        tokens.AddRange(chineseTokens);
        return tokens.Distinct(StringComparer.Ordinal).ToArray();
    }
}

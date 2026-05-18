// =============================================================================
// QueryUnderstanding/QueryNormalizer.cs — 标识符/路由标准化
// =============================================================================
// 支持:
//   - PascalCase 拆分: GetListBySpecialtyID → ["get", "list", "specialty", "id"]
//   - snake_case 拆分: EQA_EquipGRelation → ["eqa", "equip", "relation"]
//   - Route path 拆分: /api/equip-relation → ["api", "equip", "relation"]
//   - 缩写扩展: ID → id, GR → group
// =============================================================================

using System.Text;

namespace Core.QueryUnderstanding;

public sealed class QueryNormalizer
{
    private static readonly Dictionary<string, string> AbbreviationExpansions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ID"] = "id",
        ["EQA"] = "eqa",
        ["GR"] = "group",
        ["REL"] = "relation",
        ["CFG"] = "config",
        ["MGR"] = "manager",
        ["CTRL"] = "control",
        ["SVC"] = "service",
        ["DAO"] = "dao",
        ["DTO"] = "dto",
        ["VO"] = "vo",
        ["API"] = "api",
        ["SQL"] = "sql",
        ["HQL"] = "hql",
        ["UI"] = "ui",
        ["DB"] = "db",
        ["QC"] = "qualitycontrol",
        ["QA"] = "qualityassurance",
        ["PT"] = "patient",
        ["SP"] = "specialty",
        ["DEP"] = "department",
        ["DOC"] = "doctor",
        ["LAB"] = "lab",
        ["RPT"] = "report"
    };

    /// <summary>将 PascalCase 或 snake_case 拆分为小写 token 列表。</summary>
    public List<string> Tokenize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        // 检测 snake_case
        if (input.Contains('_'))
            return TokenizeSnakeCase(input);

        return TokenizePascalCase(input);
    }

    /// <summary>标准化标识符（拆分 + 扩展缩写 + 小写合并）。</summary>
    public string NormalizeIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var tokens = Tokenize(input);
        var normalized = tokens.Select(ExpandAbbreviation);
        return string.Join(" ", normalized).Trim();
    }

    /// <summary>将 route path 拆分为 token。</summary>
    public List<string> TokenizeRoute(string route)
    {
        var clean = route.TrimStart('/');
        var segments = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>();

        foreach (var segment in segments)
        {
            // 处理 kebab-case 或混合写法
            var subTokens = segment.Split('-', '_', '.');
            foreach (var sub in subTokens)
            {
                if (string.IsNullOrEmpty(sub))
                    continue;

                if (sub.Contains('{') || sub.Contains('}'))
                {
                    // 路由参数如 {id}
                    tokens.Add(sub.Replace("{", "").Replace("}", "").ToLowerInvariant());
                }
                else
                {
                    tokens.AddRange(Tokenize(sub));
                }
            }
        }

        return tokens.Where(t => t.Length > 0).ToList();
    }

    /// <summary>标准化路径为小写、去参数的规范形式。</summary>
    public string NormalizeRoute(string route)
    {
        var tokens = TokenizeRoute(route);
        return string.Join(" ", tokens.Select(ExpandAbbreviation));
    }

    /// <summary>拆分 PascalCase 标识符为小写 token。</summary>
    public List<string> SplitComponents(string input)
    {
        return Tokenize(input);
    }

    private List<string> TokenizeSnakeCase(string input)
    {
        var tokens = new List<string>();
        var segments = input.Split('_', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment))
                continue;

            // 每个 snake 段可能还是 PascalCase (如 EquipGRelation)
            if (HasUpperCaseInMiddle(segment))
                tokens.AddRange(TokenizePascalCase(segment));
            else
                tokens.Add(segment.ToLowerInvariant());
        }

        return tokens;
    }

    private List<string> TokenizePascalCase(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var prevLower = false;

        foreach (var c in input)
        {
            if (char.IsUpper(c))
            {
                if (current.Length > 0 && (prevLower || IsAcronymTransition(current, c)))
                {
                    var token = current.ToString().ToLowerInvariant();
                    if (token.Length > 0)
                        tokens.Add(token);
                    current.Clear();
                }

                current.Append(c);
                prevLower = false;
            }
            else if (c == '_')
            {
                var token = current.ToString().ToLowerInvariant();
                if (token.Length > 0)
                    tokens.Add(token);
                current.Clear();
                prevLower = false;
            }
            else
            {
                current.Append(c);
                prevLower = true;
            }
        }

        if (current.Length > 0)
        {
            var token = current.ToString().ToLowerInvariant();
            if (token.Length > 0)
                tokens.Add(token);
        }

        return tokens;
    }

    private static bool HasUpperCaseInMiddle(string input)
    {
        var first = true;
        foreach (var c in input)
        {
            if (first) { first = false; continue; }
            if (char.IsUpper(c))
                return true;
        }
        return false;
    }

    private static bool IsAcronymTransition(StringBuilder current, char nextUpper)
    {
        // 连续大写字母 → 下一个可能是新单词的开始
        if (current.Length < 2)
            return false;

        // AAAb → A, AAb (不切)
        // AaB → A, aB (切)
        var chars = current.ToString();
        return chars.Length >= 2 && char.IsUpper(chars[^1]) && char.IsUpper(chars[^2]);
    }

    private string ExpandAbbreviation(string token)
    {
        var upper = token.ToUpperInvariant();
        if (AbbreviationExpansions.TryGetValue(upper, out var expanded))
        {
            // 如果 token 本身就是完整形式，不额外扩展
            return token.Length <= 4 ? expanded : token;
        }
        return token;
    }
}

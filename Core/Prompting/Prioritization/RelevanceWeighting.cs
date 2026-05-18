// =============================================================================
// Prioritization/RelevanceWeighting.cs — compute section relevance from context
// =============================================================================

using Core.Context.Models;

namespace Core.Prompting.Prioritization;

public static class RelevanceWeighting
{
    public static double ComputePathRelevance(IReadOnlyList<string> paths, string query)
    {
        if (paths.Count == 0) return 0;
        var tokens = Tokenize(query);
        var matchCount = 0;
        var sampledPaths = paths.Take(Math.Min(paths.Count, 20));

        foreach (var path in sampledPaths)
        {
            foreach (var token in tokens)
            {
                if (path.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    matchCount++;
                    break;
                }
            }
        }

        return Math.Min(1.0, (double)matchCount / sampledPaths.Count());
    }

    public static double ComputeMethodRelevance(IReadOnlyList<string> methods, string query)
    {
        if (methods.Count == 0) return 0;
        var tokens = Tokenize(query);
        var matchCount = 0;

        foreach (var method in methods.Take(10))
        {
            foreach (var token in tokens)
            {
                if (method.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    matchCount++;
                    break;
                }
            }
        }

        return Math.Min(1.0, (double)matchCount / Math.Min(methods.Count, 10));
    }

    public static double ComputeEntityRelevance(IReadOnlyList<string> entities, string query)
    {
        if (entities.Count == 0) return 0;
        var tokens = Tokenize(query);
        var matchCount = 0;

        foreach (var entity in entities)
        {
            foreach (var token in tokens)
            {
                if (entity.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    matchCount++;
                    break;
                }
            }
        }

        return entities.Count > 0 ? (double)matchCount / entities.Count : 0;
    }

    public static double ComputeRuleRelevance(IReadOnlyList<string> rules, string query)
    {
        if (rules.Count == 0) return 0;
        var tokens = Tokenize(query);
        var matchCount = 0;

        foreach (var rule in rules.Take(20))
        {
            foreach (var token in tokens)
            {
                if (rule.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    matchCount++;
                    break;
                }
            }
        }

        return Math.Min(1.0, (double)matchCount / Math.Min(rules.Count, 20));
    }

    private static string[] Tokenize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();

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
                if (current.Count >= 2)
                    tokens.Add(new string(current.ToArray()));
                current.Clear();
            }
        }

        if (current.Count >= 2)
            tokens.Add(new string(current.ToArray()));

        return tokens.Distinct(StringComparer.Ordinal).ToArray();
    }
}

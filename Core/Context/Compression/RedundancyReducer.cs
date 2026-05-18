// =============================================================================
// Compression/RedundancyReducer.cs — deduplicate and collapse redundant context
// =============================================================================

using Core.Context.Models;

namespace Core.Context.Compression;

public static class RedundancyReducer
{
    public static IReadOnlyList<string> DeduplicatePaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var path in paths)
        {
            var normalized = NormalizePath(path);
            if (seen.Add(normalized))
                result.Add(path);
        }

        return result;
    }

    public static IReadOnlyList<string> MergeMethods(IEnumerable<string> methods)
    {
        var signatureMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var method in methods)
        {
            var sig = ExtractSignature(method);
            if (!signatureMap.TryGetValue(sig, out var list))
            {
                list = new List<string>();
                signatureMap[sig] = list;
            }
            list.Add(method);
        }

        var result = new List<string>();
        foreach (var (sig, variants) in signatureMap)
        {
            if (variants.Count == 1)
            {
                result.Add(variants[0]);
            }
            else
            {
                var merged = MergeVariants(sig, variants);
                result.Add(merged);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> CollapseEntities(IEnumerable<string> entities)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var entity in entities)
        {
            var key = entity.Trim();
            if (!seen.ContainsKey(key))
            {
                seen[key] = key;
                result.Add(key);
            }
        }

        return result;
    }

    public static (IReadOnlyList<string> Paths, IReadOnlyList<string> Methods,
        IReadOnlyList<string> Entities, IReadOnlyList<string> Tables,
        IReadOnlyList<string> Rules, int OriginalCount, int ReducedCount)
        ReduceAll(
            IEnumerable<string> paths,
            IEnumerable<string> methods,
            IEnumerable<string> entities,
            IEnumerable<string> tables,
            IEnumerable<string> rules)
    {
        var pathList = paths.ToList();
        var methodList = methods.ToList();
        var entityList = entities.ToList();
        var tableList = tables.ToList();
        var ruleList = rules.ToList();

        var originalCount = pathList.Count + methodList.Count + entityList.Count + tableList.Count + ruleList.Count;

        var reducedPaths = DeduplicatePaths(pathList);
        var reducedMethods = MergeMethods(methodList);
        var reducedEntities = CollapseEntities(entityList);
        var reducedTables = CollapseEntities(tableList);
        var reducedRules = DeduplicatePaths(ruleList);

        var reducedCount = reducedPaths.Count + reducedMethods.Count + reducedEntities.Count + reducedTables.Count + reducedRules.Count;

        return (reducedPaths, reducedMethods, reducedEntities, reducedTables, reducedRules, originalCount, reducedCount);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace("  ", " ")
                   .Replace(" → ", "->")
                   .Replace(" ⇢ ", "->")
                   .Replace(" ⤖ ", "->")
                   .Replace(" · ", "->")
                   .Trim();
    }

    private static string ExtractSignature(string method)
    {
        var lines = method.Split('\n');
        foreach (var line in lines)
        {
            var t = line.Trim();
            var parenIdx = t.IndexOf('(');
            if (parenIdx < 0) continue;
            var sig = t[..(parenIdx + 1)];
            return sig;
        }
        return method;
    }

    private static string MergeVariants(string signature, List<string> variants)
    {
        if (variants.Count == 1) return variants[0];

        var uniqueBodies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in variants)
        {
            var lines = v.Split('\n');
            var bodyLines = lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l));
            foreach (var bl in bodyLines)
                uniqueBodies.Add(bl.Trim());
        }

        return $"{signature}\n  // merged from {variants.Count} identical signatures\n  {string.Join("\n  ", uniqueBodies)}";
    }
}

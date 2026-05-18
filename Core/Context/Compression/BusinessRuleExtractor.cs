// =============================================================================
// Compression/BusinessRuleExtractor.cs — heuristic business rule extraction
// =============================================================================

using System.Text;
using Core.Context.Models;

namespace Core.Context.Compression;

public static class BusinessRuleExtractor
{
    public static ContextCompressionResult Extract(string content, IReadOnlyList<string> sourceChunkIds)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ContextCompressionResult
            {
                OriginalContent = content,
                CompressedContent = "",
                OriginalTokens = 0,
                CompressedTokens = 0,
                Strategy = "empty",
                SourceChunkIds = sourceChunkIds
            };
        }

        var lines = content.Split('\n');
        var sb = new StringBuilder();
        var ruleSet = new HashSet<string>(StringComparer.Ordinal);
        var foundRules = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var category = ClassifyLine(line);
            if (category is null) continue;

            var normalized = NormalizeLine(line);
            if (!ruleSet.Add(normalized)) continue;

            foundRules.Add($"  [{category}] {normalized}");
        }

        if (foundRules.Count == 0)
        {
            return new ContextCompressionResult
            {
                OriginalContent = content,
                CompressedContent = "",
                OriginalTokens = Budgeting.ContextBudgetEstimator.Estimate(content),
                CompressedTokens = 0,
                Strategy = "no_rules_found",
                SourceChunkIds = sourceChunkIds
            };
        }

        var compressed = string.Join('\n', foundRules);

        return new ContextCompressionResult
        {
            OriginalContent = content,
            CompressedContent = compressed,
            OriginalTokens = Budgeting.ContextBudgetEstimator.Estimate(content),
            CompressedTokens = Budgeting.ContextBudgetEstimator.Estimate(compressed),
            Strategy = "BusinessRuleExtractor",
            SourceChunkIds = sourceChunkIds
        };
    }

    public static IReadOnlyList<string> ExtractRules(string content)
    {
        var result = Extract(content, Array.Empty<string>());
        if (string.IsNullOrEmpty(result.CompressedContent)) return Array.Empty<string>();
        return result.CompressedContent.Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    private static string? ClassifyLine(string line)
    {
        if (line.Contains("throw new BusinessException", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("throw new ValidationException", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("throw new", StringComparison.Ordinal) && IsBusinessException(line))
            return "Exception";

        if (line.Contains("Validate(", StringComparison.Ordinal) ||
            line.Contains("Validator", StringComparison.Ordinal) ||
            line.Contains("Validation(", StringComparison.Ordinal))
            return "Validation";

        if (line.Contains("Audit", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains(".Log(", StringComparison.Ordinal) || line.Contains(".Write(", StringComparison.Ordinal)))
            return "Audit";

        if (line.Contains("Approval", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Approve(", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Reject(", StringComparison.OrdinalIgnoreCase))
            return "Approval";

        if (line.Contains("Status", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains('=') || line.Contains(".Status", StringComparison.Ordinal)))
            return "StatusTransition";

        if (line.Contains("Permission", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Authorize", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("HasRole", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("CheckPermission", StringComparison.OrdinalIgnoreCase))
            return "Permission";

        if (line.Contains("if (", StringComparison.Ordinal) &&
            (line.Contains("== null", StringComparison.Ordinal) ||
             line.Contains("!= null", StringComparison.Ordinal) ||
             line.Contains("Count == 0", StringComparison.Ordinal) ||
             line.Contains(".Any()", StringComparison.Ordinal)))
            return "Guard";

        if (line.Contains("throw new", StringComparison.Ordinal))
            return "Error";

        return null;
    }

    private static bool IsBusinessException(string line)
    {
        return line.Contains("BusinessException", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("DomainException", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("AppException", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("BussinessException", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLine(string line)
    {
        var result = line.Trim();
        if (result.EndsWith(';')) result = result[..^1];
        if (result.EndsWith('{')) result = result[..^1].TrimEnd();
        if (result.StartsWith("if (", StringComparison.Ordinal) && result.EndsWith(')'))
            result = "condition: " + result[4..^1];
        return result.Length > 120 ? result[..117] + "..." : result;
    }
}

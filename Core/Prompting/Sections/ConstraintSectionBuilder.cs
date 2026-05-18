// =============================================================================
// Sections/ConstraintSectionBuilder.cs — builds "Constraints / Business Rules" section
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;

namespace Core.Prompting.Sections;

public static class ConstraintSectionBuilder
{
    public static Models.PromptSection Build(StructuredContext context, int maxRules = 15)
    {
        var sb = new StringBuilder();

        if (context.BusinessRules.Count == 0)
        {
            sb.AppendLine("No business rules or constraints found in the current context.");
        }
        else
        {
            var byCategory = GroupByCategory(context.BusinessRules);

            foreach (var (category, rules) in byCategory)
            {
                sb.AppendLine($"### {category}");
                foreach (var rule in rules.Take(maxRules))
                    sb.AppendLine($"- {rule}");
                sb.AppendLine();
            }

            sb.AppendLine($"*{context.BusinessRules.Count} total rules extracted*");
        }

        var content = sb.ToString().TrimEnd();

        return new Models.PromptSection
        {
            SectionId = "business-rules",
            Title = "Business Rules & Constraints",
            Kind = Models.PromptSectionKind.BusinessRules,
            Content = content,
            Priority = 5,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = context.BusinessRules.Count > 0 ? 1.0 : 0,
            CompressionRatio = context.BusinessRules.Count > 0 ? 0.15 : 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }

    private static Dictionary<string, List<string>> GroupByCategory(IReadOnlyList<string> rules)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            var category = "General Rules";

            if (rule.StartsWith("  [Exception]", StringComparison.Ordinal))
                category = "Exceptions";
            else if (rule.StartsWith("  [Validation]", StringComparison.Ordinal))
                category = "Validation";
            else if (rule.StartsWith("  [Audit]", StringComparison.Ordinal))
                category = "Audit";
            else if (rule.StartsWith("  [Approval]", StringComparison.Ordinal))
                category = "Approval";
            else if (rule.StartsWith("  [StatusTransition]", StringComparison.Ordinal))
                category = "Status Transitions";
            else if (rule.StartsWith("  [Permission]", StringComparison.Ordinal))
                category = "Permissions";
            else if (rule.StartsWith("  [Guard]", StringComparison.Ordinal))
                category = "Guard Clauses";

            if (!result.TryGetValue(category, out var list))
            {
                list = new List<string>();
                result[category] = list;
            }
            list.Add(rule);
        }

        return result;
    }
}

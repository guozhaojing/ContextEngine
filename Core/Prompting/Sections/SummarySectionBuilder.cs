// =============================================================================
// Sections/SummarySectionBuilder.cs — builds "Summary" section
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;

namespace Core.Prompting.Sections;

public static class SummarySectionBuilder
{
    public static Models.PromptSection Build(StructuredContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine(context.Summary);
        sb.AppendLine();

        var stats = new List<string>();
        if (context.SemanticPaths.Count > 0)
            stats.Add($"{context.SemanticPaths.Count} paths");
        if (context.Routes.Count > 0)
            stats.Add($"{context.Routes.Count} routes");
        if (context.Entities.Count > 0)
            stats.Add($"{context.Entities.Count} entities");
        if (context.Tables.Count > 0)
            stats.Add($"{context.Tables.Count} tables");
        if (context.BusinessRules.Count > 0)
            stats.Add($"{context.BusinessRules.Count} rules");
        if (context.CompressedMethods.Count > 0)
            stats.Add($"{context.CompressedMethods.Count} methods");

        if (stats.Count > 0)
            sb.AppendLine($"*Context includes: {string.Join(", ", stats)}*");

        sb.AppendLine($"*Estimated tokens: {context.TokenEstimate}*");

        var content = sb.ToString().TrimEnd();

        return new Models.PromptSection
        {
            SectionId = "summary",
            Title = "Context Summary",
            Kind = Models.PromptSectionKind.Summary,
            Content = content,
            Priority = 2,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = 1.0,
            CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }
}

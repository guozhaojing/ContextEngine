// =============================================================================
// Sections/EntitySectionBuilder.cs — builds "Entities & Tables" section
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;

namespace Core.Prompting.Sections;

public static class EntitySectionBuilder
{
    public static Models.PromptSection Build(StructuredContext context, int maxEntities = 30)
    {
        var sb = new StringBuilder();

        if (context.Entities.Count == 0 && context.Tables.Count == 0)
        {
            sb.AppendLine("No entity or table information available in the current context.");
        }
        else
        {
            if (context.Entities.Count > 0)
            {
                sb.AppendLine("### Entities");
                foreach (var entity in context.Entities.Take(maxEntities))
                    sb.AppendLine($"- `{entity}`");
                sb.AppendLine();
            }

            if (context.Tables.Count > 0)
            {
                sb.AppendLine("### Tables");
                foreach (var table in context.Tables.Take(maxEntities))
                    sb.AppendLine($"- `{table}`");
                sb.AppendLine();
            }

            if (context.Metadata.TryGetValue("candidates", out var candidates))
                sb.AppendLine($"*Retrieved from {candidates} source chunks*");
        }

        var content = sb.ToString().TrimEnd();

        return new Models.PromptSection
        {
            SectionId = "entities-tables",
            Title = "Entities & Tables",
            Kind = Models.PromptSectionKind.EntitiesTables,
            Content = content,
            Priority = 6,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = (context.Entities.Count + context.Tables.Count) > 0 ? 1.0 : 0,
            CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }
}

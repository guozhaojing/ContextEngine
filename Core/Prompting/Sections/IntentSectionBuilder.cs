// =============================================================================
// Sections/IntentSectionBuilder.cs — builds "User Intent" reasoning section
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;

namespace Core.Prompting.Sections;

public static class IntentSectionBuilder
{
    public static Models.PromptSection Build(StructuredContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Query**: {context.Query}");
        sb.AppendLine();
        sb.AppendLine($"**Detected Intent**: {context.Intent}");
        sb.AppendLine();

        if (context.Metadata.TryGetValue("candidates", out var candidates))
            sb.AppendLine($"**Relevant Code Contexts**: {candidates} candidates retrieved");

        if (context.Metadata.TryGetValue("budget_total", out var budget))
            sb.AppendLine($"**Context Budget**: {budget} tokens allocated");

        if (context.Metadata.TryGetValue("semantic_compression", out var compression))
            sb.AppendLine($"**Compression**: {compression}");

        var content = sb.ToString().TrimEnd();

        return new Models.PromptSection
        {
            SectionId = "user-intent",
            Title = "User Intent",
            Kind = Models.PromptSectionKind.UserIntent,
            Content = content,
            Priority = 10,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = 1.0,
            CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }
}

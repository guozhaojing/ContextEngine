// =============================================================================
// Sections/MethodSectionBuilder.cs — builds "Important Methods" section
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;

namespace Core.Prompting.Sections;

public static class MethodSectionBuilder
{
    public static Models.PromptSection Build(StructuredContext context, int maxMethods = 20)
    {
        var sb = new StringBuilder();

        if (context.CompressedMethods.Count == 0)
        {
            sb.AppendLine("No compressed method summaries available.");
        }
        else
        {
            sb.AppendLine($"**{context.CompressedMethods.Count}** relevant methods summarized:");
            sb.AppendLine();

            var count = 0;
            foreach (var method in context.CompressedMethods.Take(maxMethods))
            {
                count++;
                var header = ExtractHeader(method);

                sb.AppendLine($"#### {count}. {header}");
                sb.AppendLine("```csharp");
                sb.AppendLine(method);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (context.CompressedMethods.Count > maxMethods)
                sb.AppendLine($"... and {context.CompressedMethods.Count - maxMethods} more methods");
        }

        var content = sb.ToString().TrimEnd();

        return new Models.PromptSection
        {
            SectionId = "important-methods",
            Title = "Important Methods",
            Kind = Models.PromptSectionKind.ImportantMethods,
            Content = content,
            Priority = 7,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = context.CompressedMethods.Count > 0 ? 1.0 : 0,
            CompressionRatio = context.Metadata.TryGetValue("compression_ratio", out var r) && double.TryParse(r, out var ratio) ? ratio : 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }

    private static string ExtractHeader(string method)
    {
        var newlineIdx = method.IndexOf('\n');
        if (newlineIdx > 0)
            return method[..newlineIdx].Trim();
        return method.Length > 80 ? method[..77] + "..." : method;
    }
}

// =============================================================================
// Sections/SemanticPathSectionBuilder.cs — builds "Semantic Paths" section
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;

namespace Core.Prompting.Sections;

public static class SemanticPathSectionBuilder
{
    public static Models.PromptSection Build(StructuredContext context, int maxPaths = 15)
    {
        var sb = new StringBuilder();

        if (context.SemanticPaths.Count == 0)
        {
            sb.AppendLine("No semantic paths available. Consider expanding your query or checking entity mappings.");
        }
        else
        {
            sb.AppendLine($"**{context.SemanticPaths.Count}** semantic paths mapped from routes to data access:");
            sb.AppendLine();

            var grouped = GroupByLayer(context.SemanticPaths);

            if (grouped.TryGetValue("forward", out var forwardPaths) && forwardPaths.Count > 0)
            {
                sb.AppendLine("#### Route → Entity/Table Paths");
                foreach (var path in forwardPaths.Take(maxPaths))
                    sb.AppendLine($"- {path}");
                sb.AppendLine();
            }

            if (grouped.TryGetValue("data", out var dataPaths) && dataPaths.Count > 0)
            {
                sb.AppendLine("#### Data Access Paths (nh:entity-access)");
                foreach (var path in dataPaths.Take(maxPaths))
                    sb.AppendLine($"- {path}");
                sb.AppendLine();
            }
        }

        var content = sb.ToString().TrimEnd();

        return new Models.PromptSection
        {
            SectionId = "semantic-paths",
            Title = "Semantic Paths",
            Kind = Models.PromptSectionKind.SemanticPaths,
            Content = content,
            Priority = 8,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = context.SemanticPaths.Count > 0 ? 1.0 : 0,
            CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }

    private static Dictionary<string, List<string>> GroupByLayer(IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["forward"] = new List<string>(),
            ["data"] = new List<string>()
        };

        foreach (var path in paths)
        {
            if (path.Contains("⇢", StringComparison.Ordinal) || path.Contains("nh:entity-access", StringComparison.Ordinal))
                result["data"].Add(path);
            else
                result["forward"].Add(path);
        }

        return result;
    }
}

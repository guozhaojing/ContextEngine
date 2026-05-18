// =============================================================================
// Sections/BusinessContextSectionBuilder.cs — builds "Business Context" section
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Retrieval.Chunking;
using Core.Retrieval.Retrieval;

namespace Core.Prompting.Sections;

public static class BusinessContextSectionBuilder
{
    public static Models.PromptSection Build(StructuredContext context, RetrievalResult? retrievalResult = null)
    {
        var sb = new StringBuilder();
        var chunkIds = new List<string>();

        if (context.Routes.Count > 0)
        {
            sb.AppendLine("### Entry Points / Routes");
            foreach (var route in context.Routes.Take(10))
                sb.AppendLine($"- {route}");
            sb.AppendLine();
        }

        if (context.Entities.Count > 0 || context.Tables.Count > 0)
        {
            sb.AppendLine("### Domain Model Coverage");
            if (context.Entities.Count > 0)
                sb.AppendLine($"- **Entities** ({context.Entities.Count}): {string.Join(", ", context.Entities.Take(15))}");
            if (context.Tables.Count > 0)
                sb.AppendLine($"- **Tables** ({context.Tables.Count}): {string.Join(", ", context.Tables.Take(15))}");
            sb.AppendLine();
        }

        if (retrievalResult is not null)
        {
            var entityChunks = retrievalResult.Candidates
                .Where(c => c.Chunk.Kind == ChunkKind.EntityAccess)
                .ToList();

            if (entityChunks.Count > 0)
            {
                sb.AppendLine("### Data Access Patterns");
                foreach (var c in entityChunks.Take(5))
                {
                    sb.AppendLine($"- `{c.Chunk.Title}` (relevance: {c.FusedScore:F2})");
                    chunkIds.Add(c.Chunk.ChunkId);
                }
            }
        }

        var content = sb.ToString().TrimEnd();

        if (string.IsNullOrEmpty(content))
        {
            content = "No business context data available from retrieval results.";
        }

        return new Models.PromptSection
        {
            SectionId = "business-context",
            Title = "Business Context",
            Kind = Models.PromptSectionKind.BusinessContext,
            Content = content,
            Priority = 9,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = context.Entities.Count > 0 ? 0.9 : 0.5,
            CompressionRatio = 1.0,
            SourceChunkIds = chunkIds
        };
    }
}

// =============================================================================
// Assembly/PromptContextBuilder.cs — builds prompt-optimized context string
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;

namespace Core.Context.Assembly;

public sealed class PromptContextBuilder
{
    public string BuildPromptText(StructuredContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== CODE CONTEXT ===");
        sb.AppendLine();
        sb.AppendLine($"Query: {context.Query}");
        sb.AppendLine($"Intent: {context.Intent}");
        sb.AppendLine($"Summary: {context.Summary}");
        sb.AppendLine();
        sb.AppendLine($"Token Estimate: {context.TokenEstimate}");
        sb.AppendLine();

        if (context.Routes.Count > 0)
        {
            sb.AppendLine("--- Routes ---");
            foreach (var route in context.Routes)
                sb.AppendLine(route);
            sb.AppendLine();
        }

        if (context.SemanticPaths.Count > 0)
        {
            sb.AppendLine("--- Semantic Paths ---");
            foreach (var path in context.SemanticPaths)
                sb.AppendLine(path);
            sb.AppendLine();
        }

        if (context.Entities.Count > 0 || context.Tables.Count > 0)
        {
            sb.AppendLine("--- Entities & Tables ---");
            if (context.Entities.Count > 0)
            {
                sb.Append("Entities: ");
                sb.AppendLine(string.Join(", ", context.Entities));
            }
            if (context.Tables.Count > 0)
            {
                sb.Append("Tables: ");
                sb.AppendLine(string.Join(", ", context.Tables));
            }
            sb.AppendLine();
        }

        if (context.BusinessRules.Count > 0)
        {
            sb.AppendLine("--- Business Rules ---");
            foreach (var rule in context.BusinessRules)
                sb.AppendLine(rule);
            sb.AppendLine();
        }

        if (context.CompressedMethods.Count > 0)
        {
            sb.AppendLine("--- Compressed Methods ---");
            var count = 0;
            foreach (var method in context.CompressedMethods)
            {
                count++;
                if (ContextBudgetEstimator.Estimate(sb.ToString()) > 8000)
                {
                    sb.AppendLine($"... and {context.CompressedMethods.Count - count + 1} more methods");
                    break;
                }
                sb.AppendLine(method);
                sb.AppendLine();
            }
        }

        sb.AppendLine("=== END CONTEXT ===");

        return sb.ToString();
    }

    public string BuildCompactPromptText(StructuredContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Context: {context.Summary}");
        sb.AppendLine();

        if (context.Routes.Count > 0)
        {
            sb.AppendLine("Routes:");
            foreach (var r in context.Routes)
                sb.AppendLine($"  {r}");
            sb.AppendLine();
        }

        if (context.BusinessRules.Count > 0)
        {
            sb.AppendLine("Key Rules:");
            foreach (var r in context.BusinessRules.Take(10))
                sb.AppendLine($"  {r}");
            sb.AppendLine();
        }

        if (context.Entities.Count > 0)
        {
            sb.Append("Entities: ");
            sb.AppendLine(string.Join(", ", context.Entities));
        }

        if (context.Tables.Count > 0)
        {
            sb.Append("Tables: ");
            sb.AppendLine(string.Join(", ", context.Tables));
        }

        return sb.ToString();
    }
}

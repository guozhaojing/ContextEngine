// =============================================================================
// PromptTemplates/TemplateRenderer.cs — fills template slots with context data
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;

namespace Core.Prompting.PromptTemplates;

public sealed class TemplateRenderer
{
    public string Render(PromptTemplate template, StructuredContext context, ContextPolicy policy)
    {
        var sb = new StringBuilder();

        sb.AppendLine(template.SystemInstruction);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine($"## Query: {context.Query}");
        sb.AppendLine($"**Intent**: {context.Intent}");
        sb.AppendLine($"**Policy**: {policy.PolicyName}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var slot in template.Slots)
        {
            var content = FillSlot(slot, context, policy);
            if (string.IsNullOrEmpty(content) && !slot.Required)
                continue;

            sb.AppendLine($"## {slot.SlotTitle}");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrEmpty(content) ? slot.DefaultContent : content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.AppendLine(template.OutputFormat);

        return sb.ToString();
    }

    public string RenderCompact(PromptTemplate template, StructuredContext context, ContextPolicy policy)
    {
        var sb = new StringBuilder();

        sb.AppendLine(template.SystemInstruction);
        sb.AppendLine();
        sb.AppendLine($"Query: {context.Query} | Intent: {context.Intent} | Policy: {policy.PolicyName}");
        sb.AppendLine();

        foreach (var slot in template.Slots.Where(s => s.Required))
        {
            var content = FillSlot(slot, context, policy);
            if (string.IsNullOrEmpty(content)) continue;

            var compact = TruncateToLines(content, policy.MaxTokens / template.Slots.Count);
            sb.AppendLine($"### {slot.SlotTitle}");
            sb.AppendLine(compact);
            sb.AppendLine();
        }

        sb.AppendLine(template.OutputFormat);

        return sb.ToString();
    }

    public string BuildExecutionPlan(StructuredContext context, ContextPolicy policy)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Based on the provided context, follow this execution plan:");
        sb.AppendLine();

        var step = 1;

        sb.AppendLine($"{step++}. **Understand the Intent**: The query is about '{context.Query}' with intent '{context.Intent}'.");
        sb.AppendLine();

        if (context.Routes.Count > 0)
        {
            sb.AppendLine($"{step++}. **Identify Entry Points**: {context.Routes.Count} route(s) found. Trace the request flow from entry to data access.");
            sb.AppendLine();
        }

        if (context.SemanticPaths.Count > 0)
        {
            sb.AppendLine($"{step++}. **Follow Semantic Paths**: {context.SemanticPaths.Count} path(s) mapped. Use these to understand the call chain.");
            sb.AppendLine();
        }

        if (context.Entities.Count > 0 || context.Tables.Count > 0)
        {
            sb.AppendLine($"{step++}. **Analyze Data Access**: {context.Entities.Count} entities across {context.Tables.Count} tables. Check entity mappings and data flow.");
            sb.AppendLine();
        }

        if (context.BusinessRules.Count > 0)
        {
            sb.AppendLine($"{step++}. **Apply Business Rules**: {context.BusinessRules.Count} rule(s) extracted. Validate logic against constraints.");
            sb.AppendLine();
        }

        if (context.CompressedMethods.Count > 0)
        {
            sb.AppendLine($"{step++}. **Review Key Methods**: {context.CompressedMethods.Count} method(s) provided. Focus on the most relevant ones.");
            sb.AppendLine();
        }

        sb.AppendLine($"{step++}. **Synthesize Findings**: Combine the structured context sections above to form a complete answer.");
        sb.AppendLine();
        sb.AppendLine($"{step++}. **Format Output**: {policy.OutputFormatHint}");

        return sb.ToString();
    }

    private static string FillSlot(TemplateSlot slot, StructuredContext context, ContextPolicy policy)
    {
        return slot.SlotName switch
        {
            "query_intent" => $"Query: {context.Query}\nIntent: {context.Intent}\nSummary: {context.Summary}",
            "business_context" => BuildBusinessContext(context),
            "routes" => BuildRoutesSection(context),
            "semantic_paths" => BuildPathsSection(context),
            "methods" => BuildMethodsSection(context),
            "entities_tables" => BuildEntityTableSection(context),
            "business_rules" => BuildRulesSection(context),
            "constraints" => BuildConstraintsSection(context),
            "missing_info" => BuildMissingInfoPlaceholder(context),
            "summary" => context.Summary,
            _ => string.Empty
        };
    }

    private static string BuildBusinessContext(StructuredContext context)
    {
        var sb = new StringBuilder();
        if (context.Routes.Count > 0)
        {
            sb.AppendLine("**Entry Points**:");
            foreach (var r in context.Routes.Take(5))
                sb.AppendLine($"- {r}");
        }
        if (context.Entities.Count > 0)
        {
            sb.AppendLine($"**Domain Coverage**: {context.Entities.Count} entities, {context.Tables.Count} tables");
            sb.AppendLine($"Entities: {string.Join(", ", context.Entities.Take(10))}");
            if (context.Tables.Count > 0)
                sb.AppendLine($"Tables: {string.Join(", ", context.Tables.Take(10))}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildRoutesSection(StructuredContext context)
    {
        if (context.Routes.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var route in context.Routes.Take(10))
            sb.AppendLine($"- {route}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildPathsSection(StructuredContext context)
    {
        if (context.SemanticPaths.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var path in context.SemanticPaths.Take(15))
            sb.AppendLine($"- {path}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildMethodsSection(StructuredContext context)
    {
        if (context.CompressedMethods.Count == 0) return "";
        var sb = new StringBuilder();
        var count = 0;
        foreach (var method in context.CompressedMethods.Take(20))
        {
            count++;
            var firstLine = method.Split('\n')[0];
            sb.AppendLine($"- `{firstLine.Trim()}`");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildEntityTableSection(StructuredContext context)
    {
        if (context.Entities.Count == 0 && context.Tables.Count == 0) return "";
        var sb = new StringBuilder();
        if (context.Entities.Count > 0)
            sb.AppendLine($"Entities: {string.Join(", ", context.Entities.Take(20))}");
        if (context.Tables.Count > 0)
            sb.AppendLine($"Tables: {string.Join(", ", context.Tables.Take(20))}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildRulesSection(StructuredContext context)
    {
        if (context.BusinessRules.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var rule in context.BusinessRules.Take(20))
            sb.AppendLine(rule);
        return sb.ToString().TrimEnd();
    }

    private static string BuildConstraintsSection(StructuredContext context)
    {
        var constraints = context.BusinessRules
            .Where(r => r.Contains("[Validation]", StringComparison.Ordinal) ||
                        r.Contains("[Guard]", StringComparison.Ordinal) ||
                        r.Contains("[Permission]", StringComparison.Ordinal))
            .Take(15)
            .ToList();

        if (constraints.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var c in constraints)
            sb.AppendLine(c);
        return sb.ToString().TrimEnd();
    }

    private static string BuildMissingInfoPlaceholder(StructuredContext context)
    {
        return "No significant gaps detected in the retrieved context. " +
               "If specific information is needed, consider narrowing the query scope.";
    }

    private static string TruncateToLines(string content, int maxTokens)
    {
        var tokens = ContextBudgetEstimator.Estimate(content);
        if (tokens <= maxTokens) return content;

        var lines = content.Split('\n');
        var sb = new StringBuilder();
        var currentTokens = 0;

        foreach (var line in lines)
        {
            var lineTokens = ContextBudgetEstimator.Estimate(line);
            if (currentTokens + lineTokens > maxTokens - 30) break;
            sb.AppendLine(line);
            currentTokens += lineTokens;
        }

        sb.Append("...");
        return sb.ToString();
    }
}

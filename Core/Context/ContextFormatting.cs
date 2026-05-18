using System.Text;

namespace Core.Context;

public static class ContextFormatting
{
    public static string ToMarkdown(ContextDocument doc)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Context Document: {doc.Id}");
        sb.AppendLine();
        sb.AppendLine($"**Query**: {doc.Query}");
        sb.AppendLine($"**Generated**: {doc.GeneratedAt}");
        sb.AppendLine($"**Tokens**: {doc.BudgetUsed} / {doc.BudgetMax} ({doc.Sections.Count} sections)");
        sb.AppendLine($"**Source Results**: {doc.SourceResultCount} candidates");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        for (var i = 0; i < doc.Sections.Count; i++)
        {
            var s = doc.Sections[i];
            sb.AppendLine($"## {i + 1}. {s.Title}");
            sb.AppendLine($"_(priority: {s.Priority}, tokens: {s.TokenCount}, kind: {s.Kind}, ratio: {s.CompressionRatio:P0}, relevance: {s.RelevanceScore:F2})_");
            sb.AppendLine();
            sb.AppendLine(s.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ToCompactMarkdown(ContextDocument doc)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Context for: {doc.Query}");
        sb.AppendLine();

        foreach (var s in doc.Sections.OrderByDescending(s => s.Priority))
        {
            if (s.Kind is ContextSectionKind.EntryPointDetail or
                ContextSectionKind.EntityTableSummary or
                ContextSectionKind.RouteChain or
                ContextSectionKind.SemanticPath or
                ContextSectionKind.CompressedMethod)
            {
                sb.AppendLine($"### {s.Title}");
                sb.AppendLine(s.Content);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

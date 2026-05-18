// =============================================================================
// Templates/MarkdownPromptFormatter.cs — deterministic markdown prompt formatting
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Prompting.Models;

namespace Core.Prompting.Templates;

public sealed class MarkdownPromptFormatter : PromptFormatter
{
    public MarkdownPromptFormatter(PromptAssemblyOptions? options = null) : base(options)
    {
    }

    public override string Format(PromptContext context)
    {
        if (Options.EnableCompactMode)
            return FormatCompact(context);

        return FormatDetailed(context);
    }

    public string FormatDetailed(PromptContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# ContextEngine — Prompt-Ready Context");
        sb.AppendLine();
        sb.AppendLine($"**Generated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Schema Version**: 1");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        var sections = context.ReasoningSections
            .OrderByDescending(s => s.Priority)
            .ThenByDescending(s => s.RelevanceScore)
            .ToList();

        var tokenBudget = Options.MaxPromptTokens;
        var usedTokens = 0;

        foreach (var section in sections)
        {
            if (usedTokens >= tokenBudget - 200)
            {
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine($"*Prompt budget ({tokenBudget} tokens) reached. {sections.Count - sections.IndexOf(section)} sections omitted.*");
                break;
            }

            var sectionText = BuildDetailedSection(section);
            var sectionTokens = ContextBudgetEstimator.Estimate(sectionText);

            if (usedTokens + sectionTokens > tokenBudget)
            {
                sectionText = BuildCompactSection(section);
                sectionTokens = ContextBudgetEstimator.Estimate(sectionText);
            }

            sb.Append(sectionText);
            usedTokens += sectionTokens;
        }

        if (context.MissingContextIssues.Count > 0)
        {
            sb.Append(BuildMissingContextSection(context.MissingContextIssues));
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Context Token Estimate: {context.TokenEstimate} | Budget: {tokenBudget} | Used: ~{usedTokens}*");

        var metadata = context.Metadata;
        if (metadata.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("<!-- Metadata:");
            foreach (var (key, value) in metadata.Take(15))
                sb.AppendLine($"  {key}: {value}");
            sb.AppendLine("-->");
        }

        return sb.ToString();
    }

    public string FormatCompact(PromptContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Context: {context.UserQuery}");
        sb.AppendLine($"Intent: {context.DetectedIntent}");
        sb.AppendLine();

        var prioritySections = context.ReasoningSections
            .Where(s => s.Kind is PromptSectionKind.UserIntent or
                         PromptSectionKind.BusinessContext or
                         PromptSectionKind.RelevantRoutes or
                         PromptSectionKind.EntitiesTables or
                         PromptSectionKind.BusinessRules or
                         PromptSectionKind.MissingInformation)
            .OrderByDescending(s => s.Priority)
            .ToList();

        foreach (var section in prioritySections)
        {
            sb.Append(BuildCompactSection(section));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildDetailedSection(PromptSection section)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"---");
        sb.AppendLine();
        sb.AppendLine($"## {section.Title}");
        sb.AppendLine($"> priority={section.Priority} | tokens={section.TokenEstimate} | relevance={section.RelevanceScore:F2} | compression={section.CompressionRatio:P0}");
        sb.AppendLine();

        if (section.SourceChunkIds.Count > 0)
            sb.AppendLine($"> sources: {string.Join(", ", section.SourceChunkIds.Take(10))}");
        sb.AppendLine();

        sb.AppendLine(section.Content);
        sb.AppendLine();

        return sb.ToString();
    }

    private static string BuildCompactSection(PromptSection section)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {section.Title}");
        sb.AppendLine(section.Content);
        return sb.ToString();
    }

    private static string BuildMissingContextSection(IReadOnlyList<MissingContextIssue> issues)
    {
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Missing Information");
        sb.AppendLine("> The following gaps were detected in the retrieved context:");
        sb.AppendLine();

        var severities = new[] { "Critical", "Warning", "Info" };

        foreach (var level in severities)
        {
            var levelIssues = level switch
            {
                "Critical" => issues.Where(i => i.Severity >= 0.7).ToList(),
                "Warning" => issues.Where(i => i.Severity >= 0.4 && i.Severity < 0.7).ToList(),
                _ => issues.Where(i => i.Severity < 0.4).ToList()
            };

            if (levelIssues.Count == 0) continue;

            sb.AppendLine($"### {level} ({levelIssues.Count})");
            sb.AppendLine();

            foreach (var issue in levelIssues)
            {
                var icon = level switch
                {
                    "Critical" => "🔴",
                    "Warning" => "🟡",
                    _ => "🔵"
                };

                sb.AppendLine($"- {icon} **[{issue.Kind}]** {issue.Description}");

                if (!string.IsNullOrEmpty(issue.AffectedEntity))
                    sb.AppendLine($"  - Affected Entity: `{issue.AffectedEntity}`");
                if (!string.IsNullOrEmpty(issue.AffectedRoute))
                    sb.AppendLine($"  - Affected Route: `{issue.AffectedRoute}`");
                if (!string.IsNullOrEmpty(issue.AffectedMethod))
                    sb.AppendLine($"  - Affected Method: `{issue.AffectedMethod}`");
                if (!string.IsNullOrEmpty(issue.Recommendation))
                    sb.AppendLine($"  - Recommendation: {issue.Recommendation}");

                sb.AppendLine($"  - Severity: {issue.Severity:F2}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

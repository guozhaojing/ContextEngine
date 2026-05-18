// =============================================================================
// Prompt/PromptQualityScorer.cs — scores prompt quality across 4 dimensions
// =============================================================================

using Core.Prompting.Models;

namespace Core.Evaluation.Prompt;

public sealed class PromptQualityScorer
{
    public PromptQualityScores Score(FinalPrompt finalPrompt)
    {
        var coherence = ScoreCoherence(finalPrompt);
        var completeness = ScoreCompleteness(finalPrompt);
        var structural = ScoreStructural(finalPrompt);
        var actionability = ScoreActionability(finalPrompt);

        return new PromptQualityScores
        {
            Coherence = coherence,
            Completeness = completeness,
            Structural = structural,
            Actionability = actionability,
            Overall = Math.Round((coherence + completeness + structural + actionability) / 4.0, 3)
        };
    }

    public PromptQualityReport GenerateReport(FinalPrompt finalPrompt)
    {
        var scores = Score(finalPrompt);
        var details = new List<QualityDetail>();

        details.AddRange(ExplainCoherence(finalPrompt, scores.Coherence));
        details.AddRange(ExplainCompleteness(finalPrompt, scores.Completeness));
        details.AddRange(ExplainStructural(finalPrompt, scores.Structural));
        details.AddRange(ExplainActionability(finalPrompt, scores.Actionability));

        return new PromptQualityReport
        {
            ReportId = $"quality-{DateTime.Now:yyyyMMddHHmmss}",
            PromptId = finalPrompt.PromptId,
            Query = finalPrompt.Query,
            Scores = scores,
            Details = details,
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Scoring Dimensions
    // ═══════════════════════════════════════════════════════════════

    private static double ScoreCoherence(FinalPrompt prompt)
    {
        var score = 1.0;
        var sections = prompt.Sections;

        if (sections.Count == 0) return 0;

        if (!HasIntentSection(sections)) score -= 0.2;
        if (!HasExecutionPlan(prompt)) score -= 0.2;
        if (sections.Count < 3) score -= 0.3;

        var ordered = sections.OrderBy(s => s.Priority).ToList();
        var priorityMonotonic = true;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Priority < ordered[i - 1].Priority)
            {
                priorityMonotonic = false;
                break;
            }
        }
        if (!priorityMonotonic) score -= 0.15;

        return Math.Max(0, Math.Round(score, 3));
    }

    private static double ScoreCompleteness(FinalPrompt prompt)
    {
        var score = 1.0;
        var sections = prompt.Sections;
        var content = prompt.Content;

        if (!HasIntentSection(sections)) score -= 0.2;
        if (string.IsNullOrEmpty(prompt.ExecutionPlan)) score -= 0.2;
        if (prompt.Anchors.Methods.Count == 0 && prompt.Anchors.Routes.Count == 0) score -= 0.15;
        if (prompt.Anchors.Entities.Count == 0 && prompt.Anchors.Tables.Count == 0) score -= 0.1;
        if (string.IsNullOrEmpty(prompt.ExpectedOutputFormat)) score -= 0.15;
        if (string.IsNullOrEmpty(prompt.IntentSummary)) score -= 0.1;
        if (content.Length < 500) score -= 0.1;

        return Math.Max(0, Math.Round(score, 3));
    }

    private static double ScoreStructural(FinalPrompt prompt)
    {
        var score = 1.0;
        var content = prompt.Content;

        if (!content.Contains("## ", StringComparison.Ordinal)) score -= 0.3;
        if (!content.Contains("---", StringComparison.Ordinal)) score -= 0.1;
        if (!content.Contains("Query", StringComparison.OrdinalIgnoreCase)) score -= 0.15;
        if (!content.Contains("Intent", StringComparison.OrdinalIgnoreCase)) score -= 0.15;
        if (prompt.Sections.Count < 2) score -= 0.2;

        var sectionCount = CountMarkdownSections(content);
        if (sectionCount < 3) score -= 0.1;

        return Math.Max(0, Math.Round(score, 3));
    }

    private static double ScoreActionability(FinalPrompt prompt)
    {
        var score = 1.0;
        var content = prompt.Content;

        if (string.IsNullOrEmpty(prompt.ExecutionPlan)) score -= 0.3;

        var hasCodeBlocks = content.Contains("```", StringComparison.Ordinal) ||
                            prompt.Anchors.Methods.Count > 0;
        if (!hasCodeBlocks) score -= 0.15;

        if (string.IsNullOrEmpty(prompt.ExpectedOutputFormat)) score -= 0.2;

        var hasActionVerbs = content.Contains("identify", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("analyze", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("implement", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("recommend", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("trace", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("map", StringComparison.OrdinalIgnoreCase) ||
                             content.Contains("audit", StringComparison.OrdinalIgnoreCase);
        if (!hasActionVerbs) score -= 0.15;

        if (prompt.Constraints.Count > 0) score += 0.1;

        return Math.Min(1.0, Math.Max(0, Math.Round(score, 3)));
    }

    // ═══════════════════════════════════════════════════════════════
    // Explanation
    // ═══════════════════════════════════════════════════════════════

    private static List<QualityDetail> ExplainCoherence(FinalPrompt prompt, double score)
    {
        var details = new List<QualityDetail>();
        if (score < 0.5)
            details.Add(new QualityDetail { Dimension = "Coherence", Finding = "Section ordering or logical flow is weak.", Severity = "warning" });
        if (!HasIntentSection(prompt.Sections))
            details.Add(new QualityDetail { Dimension = "Coherence", Finding = "Missing intent section — prompt lacks user goal context.", Severity = "warning" });
        if (!HasExecutionPlan(prompt))
            details.Add(new QualityDetail { Dimension = "Coherence", Finding = "Missing execution plan — model has no step-by-step guidance.", Severity = "warning" });
        if (score >= 0.8)
            details.Add(new QualityDetail { Dimension = "Coherence", Finding = "Coherent section flow with clear logical progression.", Severity = "info" });
        return details;
    }

    private static List<QualityDetail> ExplainCompleteness(FinalPrompt prompt, double score)
    {
        var details = new List<QualityDetail>();
        if (prompt.Anchors.Methods.Count == 0)
            details.Add(new QualityDetail { Dimension = "Completeness", Finding = "No method code anchors — missing concrete code references.", Severity = "warning" });
        if (string.IsNullOrEmpty(prompt.ExpectedOutputFormat))
            details.Add(new QualityDetail { Dimension = "Completeness", Finding = "Missing expected output format — model output may be unstructured.", Severity = "warning" });
        if (prompt.Content.Length < 500)
            details.Add(new QualityDetail { Dimension = "Completeness", Finding = "Prompt content is very short (< 500 chars) — may lack sufficient context.", Severity = "warning" });
        if (score >= 0.8)
            details.Add(new QualityDetail { Dimension = "Completeness", Finding = "All required sections and anchors present.", Severity = "info" });
        return details;
    }

    private static List<QualityDetail> ExplainStructural(FinalPrompt prompt, double score)
    {
        var details = new List<QualityDetail>();
        if (!prompt.Content.Contains("## ", StringComparison.Ordinal))
            details.Add(new QualityDetail { Dimension = "Structural", Finding = "No markdown section headers (##) found — output will be unstructured.", Severity = "error" });
        if (CountMarkdownSections(prompt.Content) < 3)
            details.Add(new QualityDetail { Dimension = "Structural", Finding = "Too few markdown sections — prompt may be monolithic.", Severity = "warning" });
        if (score >= 0.8)
            details.Add(new QualityDetail { Dimension = "Structural", Finding = "Well-structured markdown with clear section hierarchy.", Severity = "info" });
        return details;
    }

    private static List<QualityDetail> ExplainActionability(FinalPrompt prompt, double score)
    {
        var details = new List<QualityDetail>();
        if (string.IsNullOrEmpty(prompt.ExecutionPlan))
            details.Add(new QualityDetail { Dimension = "Actionability", Finding = "No execution plan — model cannot determine action order.", Severity = "error" });
        if (!prompt.Content.Contains("```", StringComparison.Ordinal) && prompt.Anchors.Methods.Count == 0)
            details.Add(new QualityDetail { Dimension = "Actionability", Finding = "No code anchors — model has no target code to act upon.", Severity = "warning" });
        if (prompt.Constraints.Count > 0)
            details.Add(new QualityDetail { Dimension = "Actionability", Finding = $"Constraints ({prompt.Constraints.Count}) guide model behavior.", Severity = "info" });
        if (score >= 0.8)
            details.Add(new QualityDetail { Dimension = "Actionability", Finding = "Clear actionable guidance with constraints and anchors.", Severity = "info" });
        return details;
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static bool HasIntentSection(IReadOnlyList<PromptSection> sections)
    {
        return sections.Any(s => s.Kind == PromptSectionKind.UserIntent);
    }

    private static bool HasExecutionPlan(FinalPrompt prompt)
    {
        return !string.IsNullOrEmpty(prompt.ExecutionPlan) &&
               prompt.ExecutionPlan.Length > 50;
    }

    private static int CountMarkdownSections(string content)
    {
        var count = 0;
        foreach (var line in content.Split('\n'))
        {
            if (line.TrimStart().StartsWith("## ", StringComparison.Ordinal))
                count++;
        }
        return count;
    }
}

public sealed class PromptQualityScores
{
    public double Coherence { get; init; }
    public double Completeness { get; init; }
    public double Structural { get; init; }
    public double Actionability { get; init; }
    public double Overall { get; init; }
}

public sealed class QualityDetail
{
    public required string Dimension { get; init; }
    public required string Finding { get; init; }
    public string Severity { get; init; } = "info";
}

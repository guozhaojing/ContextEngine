// =============================================================================
// Detection/ContextGapAnalyzer.cs — analyzes context completeness
// =============================================================================

using Core.Context.Models;
using Core.Prompting.Models;

namespace Core.Prompting.Detection;

public sealed class ContextGapAnalyzer
{
    private readonly MissingContextDetector _detector;

    public ContextGapAnalyzer()
    {
        _detector = new MissingContextDetector();
    }

    public ContextGapReport Analyze(StructuredContext context)
    {
        var issues = _detector.Detect(context);
        var completenessScore = CalculateCompleteness(context, issues);

        return new ContextGapReport
        {
            ContextQuery = context.Query,
            Intent = context.Intent,
            Issues = issues,
            CompletenessScore = completenessScore,
            TotalIssues = issues.Count,
            CriticalIssues = issues.Count(i => i.Severity >= 0.7),
            WarningIssues = issues.Count(i => i.Severity >= 0.4 && i.Severity < 0.7),
            InfoIssues = issues.Count(i => i.Severity < 0.4),
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private static double CalculateCompleteness(
        StructuredContext context,
        IReadOnlyList<MissingContextIssue> issues)
    {
        var dimensions = 0;
        var scores = new List<double>();

        if (context.SemanticPaths.Count > 0)
        {
            scores.Add(1.0);
            dimensions++;
        }
        else
        {
            scores.Add(0.3);
            dimensions++;
        }

        if (context.Routes.Count > 0)
        {
            scores.Add(1.0);
            dimensions++;
        }
        else if (context.Entities.Count > 0)
        {
            scores.Add(0.5);
            dimensions++;
        }

        if (context.Entities.Count > 0)
        {
            scores.Add(1.0);
            dimensions++;
        }

        if (context.BusinessRules.Count > 0)
        {
            scores.Add(0.8);
            dimensions++;
        }

        if (context.CompressedMethods.Count > 0)
        {
            scores.Add(1.0);
            dimensions++;
        }

        if (issues.Count > 0)
        {
            var penalty = issues.Sum(i => i.Severity) / (issues.Count * 2);
            scores.Add(Math.Max(0, 1.0 - penalty));
            dimensions++;
        }

        if (dimensions == 0) return 0;

        var baseScore = scores.Sum() / dimensions;

        return Math.Max(0, Math.Min(1.0, baseScore));
    }
}

public sealed class ContextGapReport
{
    public string ContextQuery { get; init; } = "";
    public string Intent { get; init; } = "";
    public required IReadOnlyList<MissingContextIssue> Issues { get; init; }
    public double CompletenessScore { get; init; }
    public int TotalIssues { get; init; }
    public int CriticalIssues { get; init; }
    public int WarningIssues { get; init; }
    public int InfoIssues { get; init; }
    public string GeneratedAt { get; init; } = "";
}

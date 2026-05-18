// =============================================================================
// PromptAssembler.cs — main pipeline: StructuredContext → PromptContext
// =============================================================================

using Core.Context.Models;
using Core.Prompting.Detection;
using Core.Prompting.Models;
using Core.Prompting.Prioritization;
using Core.Prompting.Sections;
using Core.QueryUnderstanding;
using Core.Retrieval.Retrieval;

namespace Core.Prompting;

public sealed class PromptAssembler
{
    private readonly PromptAssemblyOptions _options;
    private readonly ContextPrioritizer _prioritizer;
    private readonly MissingContextDetector _missingDetector;
    private readonly ContextGapAnalyzer _gapAnalyzer;

    public PromptAssembler(PromptAssemblyOptions? options = null)
    {
        _options = options ?? PromptAssemblyOptions.Default;
        _prioritizer = new ContextPrioritizer();
        _missingDetector = new MissingContextDetector();
        _gapAnalyzer = new ContextGapAnalyzer();
    }

    public PromptContext Assemble(StructuredContext context, RetrievalResult? retrievalResult = null)
    {
        var intent = QueryIntentClassifier.Classify(context.Query);
        var intentLabel = context.Intent;

        var sections = new List<PromptSection>
        {
            IntentSectionBuilder.Build(context),
            BusinessContextSectionBuilder.Build(context, retrievalResult),
            SemanticPathSectionBuilder.Build(context, _options.MaxPathsPerSection),
            MethodSectionBuilder.Build(context, _options.MaxMethodsPerSection),
            EntitySectionBuilder.Build(context, _options.MaxEntitiesPerSection),
            ConstraintSectionBuilder.Build(context, _options.MaxRulesPerSection),
            SummarySectionBuilder.Build(context)
        };

        sections.Add(BuildRoutesSection(context));

        var prioritized = _prioritizer.Prioritize(sections, context.Query, intent);

        var missingIssues = _options.EnableMissingContextDetection
            ? _missingDetector.Detect(context)
            : Array.Empty<MissingContextIssue>();

        if (missingIssues.Count > 0)
        {
            prioritized = prioritized.Append(BuildMissingSection(missingIssues)).ToList();
        }

        var tokenEstimate = prioritized.Sum(s => s.TokenEstimate) +
            missingIssues.Sum(i => EstimateIssueTokens(i));

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prompt_version"] = "1",
            ["assembly_options"] = _options.EnableCompactMode ? "compact" : "detailed",
            ["missing_context_detection"] = _options.EnableMissingContextDetection ? "enabled" : "disabled",
            ["total_sections"] = prioritized.Count.ToString(),
            ["missing_issues"] = missingIssues.Count.ToString(),
            ["intent"] = intentLabel,
            ["prioritization"] = intent.ToString()
        };

        foreach (var (key, value) in context.Metadata)
        {
            if (!metadata.ContainsKey(key))
                metadata[key] = value;
        }

        return new PromptContext
        {
            UserQuery = context.Query,
            DetectedIntent = intentLabel,
            Summary = context.Summary,
            ReasoningSections = prioritized,
            SemanticPaths = context.SemanticPaths,
            ImportantMethods = context.CompressedMethods,
            Entities = context.Entities,
            Tables = context.Tables,
            BusinessRules = context.BusinessRules,
            Constraints = ExtractConstraints(context.BusinessRules),
            MissingContextIssues = missingIssues,
            TokenEstimate = tokenEstimate,
            Metadata = metadata
        };
    }

    public ContextGapReport AnalyzeGaps(StructuredContext context)
    {
        return _gapAnalyzer.Analyze(context);
    }

    private PromptSection BuildRoutesSection(StructuredContext context)
    {
        var content = context.Routes.Count > 0
            ? string.Join('\n', context.Routes.Select(r => $"- {r}"))
            : "No routes found in context.";

        return new PromptSection
        {
            SectionId = "relevant-routes",
            Title = "Relevant Routes",
            Kind = PromptSectionKind.RelevantRoutes,
            Content = content,
            Priority = 8,
            TokenEstimate = Context.Budgeting.ContextBudgetEstimator.Estimate(content),
            RelevanceScore = context.Routes.Count > 0 ? 1.0 : 0,
            CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }

    private PromptSection BuildMissingSection(IReadOnlyList<MissingContextIssue> issues)
    {
        var content = string.Join('\n',
            issues.Select(i => $"- [{i.Kind}] {i.Description} (severity: {i.Severity:F2})"));

        return new PromptSection
        {
            SectionId = "missing-information",
            Title = "Missing Information",
            Kind = PromptSectionKind.MissingInformation,
            Content = content,
            Priority = 8,
            TokenEstimate = Context.Budgeting.ContextBudgetEstimator.Estimate(content),
            RelevanceScore = 0.9,
            CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }

    private static IReadOnlyList<string> ExtractConstraints(IReadOnlyList<string> businessRules)
    {
        return businessRules
            .Where(r => r.Contains("[Validation]", StringComparison.Ordinal) ||
                        r.Contains("[Guard]", StringComparison.Ordinal) ||
                        r.Contains("[Permission]", StringComparison.Ordinal))
            .ToList();
    }

    private static int EstimateIssueTokens(MissingContextIssue issue)
    {
        return Context.Budgeting.ContextBudgetEstimator.Estimate(
            issue.Description + (issue.Recommendation ?? ""));
    }
}

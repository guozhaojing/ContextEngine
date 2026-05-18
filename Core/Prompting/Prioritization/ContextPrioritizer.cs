// =============================================================================
// Prioritization/ContextPrioritizer.cs — query-type-driven section prioritization
// =============================================================================

using Core.Prompting.Models;
using Core.QueryUnderstanding;

namespace Core.Prompting.Prioritization;

public sealed class ContextPrioritizer
{
    private readonly QueryFocusAnalyzer _focusAnalyzer;

    public ContextPrioritizer(Graph.GraphQueryService? queryService = null)
    {
        _focusAnalyzer = new QueryFocusAnalyzer(queryService);
    }

    public IReadOnlyList<PromptSection> Prioritize(
        IReadOnlyList<PromptSection> sections,
        string query,
        QueryIntent intent)
    {
        var focus = _focusAnalyzer.Analyze(query, intent);
        var weighted = new List<(PromptSection Section, double Weight)>();

        foreach (var section in sections)
        {
            var weight = ComputeWeight(section, focus);
            weighted.Add((section, weight));
        }

        var ordered = weighted
            .OrderByDescending(w => w.Weight)
            .ThenByDescending(w => w.Section.RelevanceScore)
            .Select(w =>
            {
                var s = w.Section;
                return new PromptSection
                {
                    SectionId = s.SectionId,
                    Title = s.Title,
                    Kind = s.Kind,
                    Content = s.Content,
                    Priority = (int)Math.Round(w.Weight * 10),
                    TokenEstimate = s.TokenEstimate,
                    RelevanceScore = w.Weight,
                    CompressionRatio = s.CompressionRatio,
                    SourceChunkIds = s.SourceChunkIds
                };
            })
            .ToList();

        return ordered;
    }

    private static double ComputeWeight(PromptSection section, QueryFocus focus)
    {
        var baseWeight = section.Kind switch
        {
            PromptSectionKind.UserIntent => 0.95,
            PromptSectionKind.BusinessContext => 0.85,
            PromptSectionKind.RelevantRoutes => 0.80,
            PromptSectionKind.SemanticPaths => 0.75,
            PromptSectionKind.ImportantMethods => 0.70,
            PromptSectionKind.BusinessRules => 0.65,
            PromptSectionKind.EntitiesTables => 0.60,
            PromptSectionKind.Constraints => 0.55,
            PromptSectionKind.MissingInformation => 0.50,
            PromptSectionKind.Summary => 0.40,
            _ => 0.50
        };

        var boost = section.Kind switch
        {
            PromptSectionKind.BusinessRules when focus.PrimaryCategory == "validation" => 0.25,
            PromptSectionKind.BusinessRules when focus.PrimaryCategory == "bug" => 0.20,
            PromptSectionKind.SemanticPaths when focus.PrimaryCategory == "data" => 0.20,
            PromptSectionKind.EntitiesTables when focus.PrimaryCategory == "data" => 0.20,
            PromptSectionKind.EntitiesTables when focus.PrimaryCategory == "feature" => 0.15,
            PromptSectionKind.RelevantRoutes when focus.PrimaryCategory == "feature" => 0.20,
            PromptSectionKind.RelevantRoutes when focus.PrimaryCategory == "bug" => 0.15,
            PromptSectionKind.ImportantMethods when focus.PrimaryCategory == "refactor" => 0.20,
            PromptSectionKind.ImportantMethods when focus.PrimaryCategory == "bug" => 0.15,
            _ => 0.0
        };

        return Math.Min(1.0, baseWeight + boost);
    }
}

// =============================================================================
// PromptStrategy/RefactorStrategy.cs — strategy for refactoring analysis
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;
using Core.Prompting.PromptTemplates;
using Core.QueryUnderstanding;

namespace Core.Prompting.PromptStrategy;

public sealed class RefactorStrategy : IPromptStrategy
{
    public QueryIntent SupportedIntent => QueryIntent.ImpactAnalysis;
    public string StrategyName => "Refactor";

    public PromptStrategyResult Execute(StructuredContext context, ContextPolicy policy)
    {
        var template = CreateTemplate();
        var renderer = new TemplateRenderer();
        var executionPlan = renderer.BuildExecutionPlan(context, policy);

        var finalPrompt = new FinalPrompt
        {
            PromptId = $"refactor-{DateTime.Now:yyyyMMddHHmmss}",
            Query = context.Query,
            IntentSummary = BuildIntentSummary(context),
            Content = renderer.Render(template, context, policy),
            Sections = BuildSections(context),
            Anchors = new CodeAnchors
            {
                Methods = context.CompressedMethods.Take(20).Select(FirstLine).ToList(),
                Entities = context.Entities.Take(15).ToList(),
                Routes = context.Routes.Take(8).ToList()
            },
            ExecutionPlan = executionPlan,
            ExpectedOutputFormat = "## Dependency Graph\n...\n\n## Coupling Analysis\n...\n\n## Refactoring Recommendations\n...",
            Constraints = Array.Empty<string>(),
            TokenEstimate = ContextBudgetEstimator.Estimate(executionPlan),
            StrategyName = StrategyName,
            TemplateName = template.TemplateName,
            PolicyName = policy.PolicyName,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["focus"] = "fan_out, dependencies, shared_services",
                ["priority_sections"] = "ImportantMethods, SemanticPaths, EntitiesTables"
            }
        };

        return new PromptStrategyResult
        {
            StrategyName = StrategyName,
            FinalPrompt = finalPrompt,
            Template = template,
            Policy = policy,
            Decisions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["primary_focus"] = "dependency_analysis",
                ["secondary_focus"] = "coupling_detection",
                ["section_emphasis"] = "methods_and_paths"
            }
        };
    }

    private static PromptTemplate CreateTemplate()
    {
        return new PromptTemplate
        {
            TemplateId = "refactor-v1",
            TemplateName = "Refactor Template",
            Description = "Template for refactoring analysis",
            SystemInstruction = "You are analyzing a codebase for refactoring opportunities. Focus on dependency graphs, fan-out analysis, shared services, and coupling points. Identify methods with high fan-out, duplicated logic across services, and opportunities to extract shared abstractions.",
            Slots = new[]
            {
                new TemplateSlot { SlotName = "query_intent", SlotTitle = "Refactoring Scope", Placeholder = "{query_intent}", Required = true },
                new TemplateSlot { SlotName = "methods", SlotTitle = "Methods & Dependencies", Placeholder = "{methods}", Required = true },
                new TemplateSlot { SlotName = "semantic_paths", SlotTitle = "Dependency Paths", Placeholder = "{semantic_paths}", Required = true },
                new TemplateSlot { SlotName = "entities_tables", SlotTitle = "Entities & Tables", Placeholder = "{entities_tables}", Required = true },
                new TemplateSlot { SlotName = "business_context", SlotTitle = "Service Context", Placeholder = "{business_context}", Required = false },
                new TemplateSlot { SlotName = "missing_info", SlotTitle = "Missing Context", Placeholder = "{missing_info}", Required = true }
            },
            OutputFormat = "Provide your analysis in the following format:\n\n## Dependency Graph\n[Map of dependencies between methods/services]\n\n## Coupling Analysis\n[High fan-out methods, tight coupling points]\n\n## Refactoring Recommendations\n[Concrete refactoring steps]"
        };
    }

    private static IReadOnlyList<PromptSection> BuildSections(StructuredContext context)
    {
        var sections = new List<PromptSection>
        {
            BuildSection("user-intent", "Refactoring Scope", PromptSectionKind.UserIntent, $"Query: {context.Query}\nIntent: {context.Intent}", 10)
        };

        if (context.CompressedMethods.Count > 0)
            sections.Add(BuildSection("methods", "Methods & Dependencies", PromptSectionKind.ImportantMethods,
                string.Join('\n', context.CompressedMethods.Take(20).Select(FirstLine)), 9));

        if (context.SemanticPaths.Count > 0)
            sections.Add(BuildSection("paths", "Dependency Paths", PromptSectionKind.SemanticPaths,
                string.Join('\n', context.SemanticPaths.Take(15)), 8));

        return sections;
    }

    private static PromptSection BuildSection(string id, string title, PromptSectionKind kind, string content, int priority)
    {
        return new PromptSection
        {
            SectionId = id, Title = title, Kind = kind, Content = content,
            Priority = priority, TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = string.IsNullOrEmpty(content) ? 0 : 1.0, CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }

    private static string BuildIntentSummary(StructuredContext context)
    {
        return $"Refactoring analysis for: {context.Query}\n" +
               $"Focus: fan-out, dependencies, shared services.\n" +
               $"Retrieved {context.CompressedMethods.Count} methods, {context.SemanticPaths.Count} paths.";
    }

    private static string FirstLine(string text)
    {
        var idx = text.IndexOf('\n');
        return idx >= 0 ? text[..idx].Trim() : text.Trim();
    }
}

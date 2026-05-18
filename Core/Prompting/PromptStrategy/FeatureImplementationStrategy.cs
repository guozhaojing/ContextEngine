// =============================================================================
// PromptStrategy/FeatureImplementationStrategy.cs — strategy for feature dev
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;
using Core.Prompting.PromptTemplates;
using Core.QueryUnderstanding;

namespace Core.Prompting.PromptStrategy;

public sealed class FeatureImplementationStrategy : IPromptStrategy
{
    public QueryIntent SupportedIntent => QueryIntent.RouteLookup;
    public string StrategyName => "FeatureImplementation";

    public PromptStrategyResult Execute(StructuredContext context, ContextPolicy policy)
    {
        var template = CreateTemplate();
        var renderer = new TemplateRenderer();
        var executionPlan = renderer.BuildExecutionPlan(context, policy);

        var finalPrompt = new FinalPrompt
        {
            PromptId = $"feature-{DateTime.Now:yyyyMMddHHmmss}",
            Query = context.Query,
            IntentSummary = BuildIntentSummary(context),
            Content = renderer.Render(template, context, policy),
            Sections = BuildSections(context),
            Anchors = new CodeAnchors
            {
                Routes = context.Routes.Take(10).ToList(),
                Methods = context.CompressedMethods.Take(15).Select(FirstLine).ToList(),
                Entities = context.Entities.Take(15).ToList(),
                Tables = context.Tables.Take(15).ToList()
            },
            ExecutionPlan = executionPlan,
            ExpectedOutputFormat = "## Implementation Plan\n1. ...\n2. ...\n\n## Affected Files\n...\n\n## Integration Points\n...",
            Constraints = Array.Empty<string>(),
            TokenEstimate = ContextBudgetEstimator.Estimate(executionPlan),
            StrategyName = StrategyName,
            TemplateName = template.TemplateName,
            PolicyName = policy.PolicyName,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["focus"] = "routes, services, entities, orchestration",
                ["priority_sections"] = "RelevantRoutes, BusinessContext, ImportantMethods"
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
                ["primary_focus"] = "routes_and_entry_points",
                ["secondary_focus"] = "service_orchestration",
                ["section_emphasis"] = "routes_and_methods"
            }
        };
    }

    private static PromptTemplate CreateTemplate()
    {
        return new PromptTemplate
        {
            TemplateId = "feature-v1",
            TemplateName = "Feature Implementation Template",
            Description = "Template for implementing new features",
            SystemInstruction = "You are implementing a new feature. Understand the existing routes, services, entities, and orchestration logic. Identify where the new feature fits, what methods need to be created or modified, and how it integrates with existing code.",
            Slots = new[]
            {
                new TemplateSlot { SlotName = "query_intent", SlotTitle = "Feature Request", Placeholder = "{query_intent}", Required = true },
                new TemplateSlot { SlotName = "routes", SlotTitle = "API Routes & Entry Points", Placeholder = "{routes}", Required = true },
                new TemplateSlot { SlotName = "business_context", SlotTitle = "Business Context", Placeholder = "{business_context}", Required = true },
                new TemplateSlot { SlotName = "methods", SlotTitle = "Relevant Methods & Services", Placeholder = "{methods}", Required = true },
                new TemplateSlot { SlotName = "entities_tables", SlotTitle = "Entities & Tables", Placeholder = "{entities_tables}", Required = true },
                new TemplateSlot { SlotName = "semantic_paths", SlotTitle = "Integration Paths", Placeholder = "{semantic_paths}", Required = false },
                new TemplateSlot { SlotName = "missing_info", SlotTitle = "Missing Context", Placeholder = "{missing_info}", Required = true }
            },
            OutputFormat = "Provide your analysis in the following format:\n\n## Implementation Plan\n[Step-by-step implementation plan]\n\n## Affected Files & Methods\n[List files that need changes]\n\n## Integration Points\n[How the new feature integrates with existing routes/services/repos]"
        };
    }

    private static IReadOnlyList<PromptSection> BuildSections(StructuredContext context)
    {
        var sections = new List<PromptSection>
        {
            BuildSection("user-intent", "Feature Request", PromptSectionKind.UserIntent, $"Query: {context.Query}\nIntent: {context.Intent}", 10)
        };

        if (context.Routes.Count > 0)
            sections.Add(BuildSection("routes", "API Routes", PromptSectionKind.RelevantRoutes,
                string.Join('\n', context.Routes.Take(10)), 9));

        if (context.CompressedMethods.Count > 0)
            sections.Add(BuildSection("methods", "Relevant Methods", PromptSectionKind.ImportantMethods,
                string.Join('\n', context.CompressedMethods.Take(15).Select(FirstLine)), 8));

        if (context.Entities.Count > 0)
            sections.Add(BuildSection("entities", "Entities & Tables", PromptSectionKind.EntitiesTables,
                $"Entities: {string.Join(", ", context.Entities.Take(15))}\nTables: {string.Join(", ", context.Tables.Take(15))}", 7));

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
        return $"Feature implementation for: {context.Query}\n" +
               $"Focus: routes, services, entities, orchestration.\n" +
               $"Retrieved {context.Routes.Count} routes, {context.CompressedMethods.Count} methods, {context.Entities.Count} entities.";
    }

    private static string FirstLine(string text)
    {
        var idx = text.IndexOf('\n');
        return idx >= 0 ? text[..idx].Trim() : text.Trim();
    }
}

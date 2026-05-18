// =============================================================================
// PromptStrategy/BugFixStrategy.cs — strategy for bug fixing / debugging
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;
using Core.Prompting.PromptTemplates;
using Core.QueryUnderstanding;

namespace Core.Prompting.PromptStrategy;

public sealed class BugFixStrategy : IPromptStrategy
{
    public QueryIntent SupportedIntent => QueryIntent.FlowAnalysis;
    public string StrategyName => "BugFix";

    public PromptStrategyResult Execute(StructuredContext context, ContextPolicy policy)
    {
        var template = CreateTemplate();
        var renderer = new TemplateRenderer();
        var executionPlan = renderer.BuildExecutionPlan(context, policy);

        var finalPrompt = new FinalPrompt
        {
            PromptId = $"bugfix-{DateTime.Now:yyyyMMddHHmmss}",
            Query = context.Query,
            IntentSummary = BuildIntentSummary(context),
            Content = renderer.Render(template, context, policy),
            Sections = BuildBugFixSections(context),
            Anchors = new CodeAnchors
            {
                Methods = context.CompressedMethods.Take(10).Select(FirstLine).ToList(),
                Routes = context.Routes.Take(5).ToList(),
                Entities = context.Entities.Take(10).ToList(),
                Tables = context.Tables.Take(10).ToList()
            },
            ExecutionPlan = executionPlan,
            ExpectedOutputFormat = "## Root Cause\n...\n\n## Affected Code\n...\n\n## Fix Recommendation\n...",
            Constraints = ExtractConstraints(context),
            TokenEstimate = ContextBudgetEstimator.Estimate(executionPlan),
            StrategyName = StrategyName,
            TemplateName = template.TemplateName,
            PolicyName = policy.PolicyName,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["focus"] = "validation, exception flow, guard clauses",
                ["priority_sections"] = "BusinessRules, Constraints, ImportantMethods"
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
                ["primary_focus"] = "exception_trace",
                ["secondary_focus"] = "guard_validation",
                ["section_emphasis"] = "rules_and_methods"
            }
        };
    }

    private static PromptTemplate CreateTemplate()
    {
        return new PromptTemplate
        {
            TemplateId = "bugfix-v1",
            TemplateName = "Bug Fix Template",
            Description = "Template for debugging and bug fix analysis",
            SystemInstruction = "You are analyzing a codebase to identify and fix a bug. Focus on exception flows, validation logic, guard clauses, and the methods most likely to contain the defect. Trace the call chain from entry point to the failure site. Identify root cause and recommend a fix.",
            Slots = new[]
            {
                new TemplateSlot { SlotName = "query_intent", SlotTitle = "Bug Report & Intent", Placeholder = "{query_intent}", Required = true },
                new TemplateSlot { SlotName = "business_rules", SlotTitle = "Business Rules & Validation Logic", Placeholder = "{business_rules}", Required = true },
                new TemplateSlot { SlotName = "constraints", SlotTitle = "Constraints & Guard Clauses", Placeholder = "{constraints}", Required = true },
                new TemplateSlot { SlotName = "semantic_paths", SlotTitle = "Execution Path (Call Chain)", Placeholder = "{semantic_paths}", Required = true },
                new TemplateSlot { SlotName = "methods", SlotTitle = "Key Methods", Placeholder = "{methods}", Required = true },
                new TemplateSlot { SlotName = "entities_tables", SlotTitle = "Affected Entities & Tables", Placeholder = "{entities_tables}", Required = false },
                new TemplateSlot { SlotName = "missing_info", SlotTitle = "Missing Context", Placeholder = "{missing_info}", Required = true }
            },
            OutputFormat = "Provide your analysis in the following format:\n\n## Root Cause\n[Identify the root cause based on the context]\n\n## Affected Code\n[List affected methods, entities, and routes]\n\n## Fix Recommendation\n[Propose a concrete fix]"
        };
    }

    private static IReadOnlyList<PromptSection> BuildBugFixSections(StructuredContext context)
    {
        var sections = new List<PromptSection>();
        sections.Add(BuildSection("user-intent", "Bug Report", PromptSectionKind.UserIntent, $"Query: {context.Query}\nIntent: {context.Intent}", 10));

        if (context.BusinessRules.Count > 0)
            sections.Add(BuildSection("business-rules", "Business Rules", PromptSectionKind.BusinessRules,
                string.Join('\n', context.BusinessRules.Take(20)), 9));

        if (context.SemanticPaths.Count > 0)
            sections.Add(BuildSection("execution-path", "Execution Path", PromptSectionKind.SemanticPaths,
                string.Join('\n', context.SemanticPaths.Take(10)), 8));

        if (context.CompressedMethods.Count > 0)
            sections.Add(BuildSection("key-methods", "Key Methods", PromptSectionKind.ImportantMethods,
                string.Join('\n', context.CompressedMethods.Take(15).Select(FirstLine)), 7));

        return sections;
    }

    private static PromptSection BuildSection(string id, string title, PromptSectionKind kind, string content, int priority)
    {
        return new PromptSection
        {
            SectionId = id,
            Title = title,
            Kind = kind,
            Content = content,
            Priority = priority,
            TokenEstimate = ContextBudgetEstimator.Estimate(content),
            RelevanceScore = string.IsNullOrEmpty(content) ? 0 : 1.0,
            CompressionRatio = 1.0,
            SourceChunkIds = Array.Empty<string>()
        };
    }

    private static string BuildIntentSummary(StructuredContext context)
    {
        return $"Bug analysis for: {context.Query}\n" +
               $"Focus areas: validation logic, exception flow, guard clauses, stack path.\n" +
               $"Retrieved {context.SemanticPaths.Count} paths, {context.BusinessRules.Count} rules, {context.CompressedMethods.Count} methods.";
    }

    private static IReadOnlyList<string> ExtractConstraints(StructuredContext context)
    {
        return context.BusinessRules
            .Where(r => r.Contains("[Validation]", StringComparison.Ordinal) ||
                        r.Contains("[Guard]", StringComparison.Ordinal) ||
                        r.Contains("[Permission]", StringComparison.Ordinal))
            .Take(10)
            .ToList();
    }

    private static string FirstLine(string text)
    {
        var idx = text.IndexOf('\n');
        return idx >= 0 ? text[..idx].Trim() : text.Trim();
    }
}

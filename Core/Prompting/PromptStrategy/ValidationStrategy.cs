// =============================================================================
// PromptStrategy/ValidationStrategy.cs — strategy for validation/rule analysis
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;
using Core.Prompting.PromptTemplates;
using Core.QueryUnderstanding;

namespace Core.Prompting.PromptStrategy;

public sealed class ValidationStrategy : IPromptStrategy
{
    public QueryIntent SupportedIntent => QueryIntent.ValidationLookup;
    public string StrategyName => "Validation";

    public PromptStrategyResult Execute(StructuredContext context, ContextPolicy policy)
    {
        var template = CreateTemplate();
        var renderer = new TemplateRenderer();
        var executionPlan = renderer.BuildExecutionPlan(context, policy);

        var finalPrompt = new FinalPrompt
        {
            PromptId = $"validation-{DateTime.Now:yyyyMMddHHmmss}",
            Query = context.Query,
            IntentSummary = BuildIntentSummary(context),
            Content = renderer.Render(template, context, policy),
            Sections = BuildSections(context),
            Anchors = new CodeAnchors
            {
                Methods = context.CompressedMethods.Take(15).Select(FirstLine).ToList(),
                Entities = context.Entities.Take(10).ToList(),
                Routes = context.Routes.Take(5).ToList()
            },
            ExecutionPlan = executionPlan,
            ExpectedOutputFormat = "## Validation Rules\n[List all rules with severity]\n\n## Constraints\n[List all constraints found]\n\n## Coverage Gaps\n[Areas without validation]\n\n## Hardening Recommendations\n...",
            Constraints = context.BusinessRules
                .Where(r => r.Contains("[Validation]", StringComparison.Ordinal) ||
                            r.Contains("[Guard]", StringComparison.Ordinal) ||
                            r.Contains("[Permission]", StringComparison.Ordinal))
                .Take(15)
                .ToList(),
            TokenEstimate = ContextBudgetEstimator.Estimate(executionPlan),
            StrategyName = StrategyName,
            TemplateName = template.TemplateName,
            PolicyName = policy.PolicyName,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["focus"] = "rules, constraints, guards, permissions",
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
                ["primary_focus"] = "rule_extraction",
                ["secondary_focus"] = "gap_detection",
                ["section_emphasis"] = "rules_and_constraints"
            }
        };
    }

    private static PromptTemplate CreateTemplate()
    {
        return new PromptTemplate
        {
            TemplateId = "validation-v1",
            TemplateName = "Validation Template",
            Description = "Template for validation rule analysis",
            SystemInstruction = "You are analyzing validation logic, business rules, and constraints in a codebase. Extract all validation rules, identify permission checks, audit requirements, approval workflows, and status transitions. Highlight gaps where validation is missing and recommend hardening measures.",
            Slots = new[]
            {
                new TemplateSlot { SlotName = "query_intent", SlotTitle = "Validation Scope", Placeholder = "{query_intent}", Required = true },
                new TemplateSlot { SlotName = "business_rules", SlotTitle = "Business Rules & Validation", Placeholder = "{business_rules}", Required = true },
                new TemplateSlot { SlotName = "constraints", SlotTitle = "Constraints & Permissions", Placeholder = "{constraints}", Required = true },
                new TemplateSlot { SlotName = "methods", SlotTitle = "Methods with Validation Logic", Placeholder = "{methods}", Required = true },
                new TemplateSlot { SlotName = "entities_tables", SlotTitle = "Affected Entities", Placeholder = "{entities_tables}", Required = false },
                new TemplateSlot { SlotName = "missing_info", SlotTitle = "Validation Gaps", Placeholder = "{missing_info}", Required = true }
            },
            OutputFormat = "Provide your analysis in the following format:\n\n## Validation Rules\n[Complete list of rules, categorized]\n\n## Constraints\n[Guard clauses, permissions, approvals]\n\n## Coverage Gaps\n[Methods/entities without validation]\n\n## Hardening Recommendations\n[Concrete steps to improve validation coverage]"
        };
    }

    private static IReadOnlyList<PromptSection> BuildSections(StructuredContext context)
    {
        var sections = new List<PromptSection>
        {
            BuildSection("user-intent", "Validation Scope", PromptSectionKind.UserIntent, $"Query: {context.Query}\nIntent: {context.Intent}", 10)
        };

        if (context.BusinessRules.Count > 0)
            sections.Add(BuildSection("rules", "Business Rules & Validation", PromptSectionKind.BusinessRules,
                string.Join('\n', context.BusinessRules.Take(25)), 9));

        if (context.CompressedMethods.Count > 0)
            sections.Add(BuildSection("methods", "Methods with Validation", PromptSectionKind.ImportantMethods,
                string.Join('\n', context.CompressedMethods.Take(15).Select(FirstLine)), 8));

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
        return $"Validation analysis for: {context.Query}\n" +
               $"Focus: business rules, constraints, guard clauses, permissions.\n" +
               $"Retrieved {context.BusinessRules.Count} rules, {context.CompressedMethods.Count} methods.";
    }

    private static string FirstLine(string text)
    {
        var idx = text.IndexOf('\n');
        return idx >= 0 ? text[..idx].Trim() : text.Trim();
    }
}

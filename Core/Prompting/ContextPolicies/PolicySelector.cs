// =============================================================================
// ContextPolicies/PolicySelector.cs — selects policy based on query intent
// =============================================================================

using Core.Prompting.Models;
using Core.QueryUnderstanding;

namespace Core.Prompting.ContextPolicies;

public sealed class PolicySelector
{
    private readonly Dictionary<QueryIntent, ContextPolicy> _policies;

    public PolicySelector()
    {
        _policies = new Dictionary<QueryIntent, ContextPolicy>
        {
            [QueryIntent.FlowAnalysis] = CreateBugFixPolicy(),
            [QueryIntent.ImpactAnalysis] = CreateRefactorPolicy(),
            [QueryIntent.EntityLookup] = CreateDataFlowPolicy(),
            [QueryIntent.RouteLookup] = CreateFeaturePolicy(),
            [QueryIntent.ValidationLookup] = CreateValidationPolicy(),
            [QueryIntent.Unknown] = CreateDefaultPolicy()
        };
    }

    public ContextPolicy Select(QueryIntent intent)
    {
        return _policies.TryGetValue(intent, out var policy)
            ? policy
            : CreateDefaultPolicy();
    }

    public ContextPolicy Select(string query)
    {
        var intent = QueryIntentClassifier.Classify(query);
        return Select(intent);
    }

    // ═══════════════════════════════════════════════════════════════
    // Policy Definitions
    // ═══════════════════════════════════════════════════════════════

    private static ContextPolicy CreateBugFixPolicy()
    {
        return new ContextPolicy
        {
            PolicyId = "bug-fix",
            PolicyName = "Bug Fix Policy",
            Description = "Prioritizes validation, exception flow, guard clauses, and stack paths for debugging.",
            MaxTokens = 6000,
            FocusAreas = new[] { "validation", "exception_flow", "stack_path", "guard_clauses" },
            IncludeMissingContext = true,
            IncludeExecutionPlan = true,
            OutputFormatHint = "Identify root cause, affected methods, and fix recommendation.",
            SectionRules = new[]
            {
                new PolicySectionRule { SectionKind = PromptSectionKind.UserIntent, Required = true, MaxItems = 1, Order = 1 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessRules, Required = true, MaxItems = 20, MaxTokens = 1800, Order = 2 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Constraints, Required = true, MaxItems = 15, MaxTokens = 1200, Order = 3 },
                new PolicySectionRule { SectionKind = PromptSectionKind.ImportantMethods, Required = true, MaxItems = 15, MaxTokens = 2500, Order = 4 },
                new PolicySectionRule { SectionKind = PromptSectionKind.SemanticPaths, Required = true, MaxItems = 10, MaxTokens = 1500, Order = 5 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessContext, Required = false, MaxItems = 5, Order = 6 },
                new PolicySectionRule { SectionKind = PromptSectionKind.EntitiesTables, Required = false, MaxItems = 10, Order = 7 },
                new PolicySectionRule { SectionKind = PromptSectionKind.RelevantRoutes, Required = false, MaxItems = 5, Order = 8 },
                new PolicySectionRule { SectionKind = PromptSectionKind.MissingInformation, Required = true, MaxItems = 10, MaxTokens = 800, Order = 9 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Summary, Required = true, MaxItems = 1, Order = 10 }
            }
        };
    }

    private static ContextPolicy CreateFeaturePolicy()
    {
        return new ContextPolicy
        {
            PolicyId = "feature-implementation",
            PolicyName = "Feature Implementation Policy",
            Description = "Prioritizes routes, services, entities, and orchestration methods for new feature development.",
            MaxTokens = 8000,
            FocusAreas = new[] { "routes", "services", "entities", "orchestration" },
            IncludeMissingContext = true,
            IncludeExecutionPlan = true,
            OutputFormatHint = "Provide implementation steps, affected files, and integration points.",
            SectionRules = new[]
            {
                new PolicySectionRule { SectionKind = PromptSectionKind.UserIntent, Required = true, MaxItems = 1, Order = 1 },
                new PolicySectionRule { SectionKind = PromptSectionKind.RelevantRoutes, Required = true, MaxItems = 10, MaxTokens = 1500, Order = 2 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessContext, Required = true, MaxItems = 10, MaxTokens = 1500, Order = 3 },
                new PolicySectionRule { SectionKind = PromptSectionKind.ImportantMethods, Required = true, MaxItems = 20, MaxTokens = 3000, Order = 4 },
                new PolicySectionRule { SectionKind = PromptSectionKind.EntitiesTables, Required = true, MaxItems = 20, MaxTokens = 1200, Order = 5 },
                new PolicySectionRule { SectionKind = PromptSectionKind.SemanticPaths, Required = false, MaxItems = 10, MaxTokens = 1000, Order = 6 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessRules, Required = false, MaxItems = 10, Order = 7 },
                new PolicySectionRule { SectionKind = PromptSectionKind.MissingInformation, Required = true, MaxItems = 10, MaxTokens = 800, Order = 8 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Summary, Required = true, MaxItems = 1, Order = 9 }
            }
        };
    }

    private static ContextPolicy CreateRefactorPolicy()
    {
        return new ContextPolicy
        {
            PolicyId = "refactor",
            PolicyName = "Refactor Policy",
            Description = "Prioritizes fan-out, dependencies, and shared services for refactoring analysis.",
            MaxTokens = 8000,
            FocusAreas = new[] { "fan_out", "dependencies", "shared_services" },
            IncludeMissingContext = true,
            IncludeExecutionPlan = true,
            OutputFormatHint = "Identify coupling points, dependency graphs, and refactoring opportunities.",
            SectionRules = new[]
            {
                new PolicySectionRule { SectionKind = PromptSectionKind.UserIntent, Required = true, MaxItems = 1, Order = 1 },
                new PolicySectionRule { SectionKind = PromptSectionKind.ImportantMethods, Required = true, MaxItems = 25, MaxTokens = 3500, Order = 2 },
                new PolicySectionRule { SectionKind = PromptSectionKind.SemanticPaths, Required = true, MaxItems = 15, MaxTokens = 2000, Order = 3 },
                new PolicySectionRule { SectionKind = PromptSectionKind.EntitiesTables, Required = true, MaxItems = 20, Order = 4 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessContext, Required = false, MaxItems = 10, Order = 5 },
                new PolicySectionRule { SectionKind = PromptSectionKind.RelevantRoutes, Required = false, MaxItems = 8, Order = 6 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessRules, Required = false, MaxItems = 10, Order = 7 },
                new PolicySectionRule { SectionKind = PromptSectionKind.MissingInformation, Required = true, MaxItems = 10, MaxTokens = 800, Order = 8 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Summary, Required = true, MaxItems = 1, Order = 9 }
            }
        };
    }

    private static ContextPolicy CreateDataFlowPolicy()
    {
        return new ContextPolicy
        {
            PolicyId = "data-flow",
            PolicyName = "Data Flow Policy",
            Description = "Prioritizes repositories, entity access, tables, and nh:entity-access paths for data analysis.",
            MaxTokens = 8000,
            FocusAreas = new[] { "repositories", "entity_access", "tables", "nh_paths" },
            IncludeMissingContext = true,
            IncludeExecutionPlan = true,
            OutputFormatHint = "Map data flow from API to database, identify entity relationships, and list affected tables.",
            SectionRules = new[]
            {
                new PolicySectionRule { SectionKind = PromptSectionKind.UserIntent, Required = true, MaxItems = 1, Order = 1 },
                new PolicySectionRule { SectionKind = PromptSectionKind.EntitiesTables, Required = true, MaxItems = 30, MaxTokens = 2000, Order = 2 },
                new PolicySectionRule { SectionKind = PromptSectionKind.SemanticPaths, Required = true, MaxItems = 20, MaxTokens = 2500, Order = 3 },
                new PolicySectionRule { SectionKind = PromptSectionKind.ImportantMethods, Required = true, MaxItems = 20, MaxTokens = 2000, Order = 4 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessContext, Required = true, MaxItems = 15, Order = 5 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessRules, Required = false, MaxItems = 15, Order = 6 },
                new PolicySectionRule { SectionKind = PromptSectionKind.MissingInformation, Required = true, MaxItems = 10, MaxTokens = 1000, Order = 7 },
                new PolicySectionRule { SectionKind = PromptSectionKind.RelevantRoutes, Required = false, MaxItems = 5, Order = 8 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Summary, Required = true, MaxItems = 1, Order = 9 }
            }
        };
    }

    private static ContextPolicy CreateValidationPolicy()
    {
        return new ContextPolicy
        {
            PolicyId = "validation",
            PolicyName = "Validation Policy",
            Description = "Prioritizes business rules, constraints, guard clauses, and permissions for validation analysis.",
            MaxTokens = 6000,
            FocusAreas = new[] { "rules", "constraints", "guards", "permissions" },
            IncludeMissingContext = true,
            IncludeExecutionPlan = true,
            OutputFormatHint = "List all validation rules, identify gaps, and recommend hardening measures.",
            SectionRules = new[]
            {
                new PolicySectionRule { SectionKind = PromptSectionKind.UserIntent, Required = true, MaxItems = 1, Order = 1 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessRules, Required = true, MaxItems = 25, MaxTokens = 2500, Order = 2 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Constraints, Required = true, MaxItems = 20, MaxTokens = 1500, Order = 3 },
                new PolicySectionRule { SectionKind = PromptSectionKind.ImportantMethods, Required = true, MaxItems = 15, MaxTokens = 1500, Order = 4 },
                new PolicySectionRule { SectionKind = PromptSectionKind.EntitiesTables, Required = false, MaxItems = 10, Order = 5 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessContext, Required = false, MaxItems = 5, Order = 6 },
                new PolicySectionRule { SectionKind = PromptSectionKind.MissingInformation, Required = true, MaxItems = 10, MaxTokens = 800, Order = 7 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Summary, Required = true, MaxItems = 1, Order = 8 }
            }
        };
    }

    private static ContextPolicy CreateDefaultPolicy()
    {
        return new ContextPolicy
        {
            PolicyId = "default",
            PolicyName = "Default Policy",
            Description = "Balanced policy for general code exploration.",
            MaxTokens = 8000,
            FocusAreas = new[] { "routes", "entities", "methods", "rules" },
            IncludeMissingContext = true,
            IncludeExecutionPlan = true,
            OutputFormatHint = "Provide a comprehensive overview of the relevant code context.",
            SectionRules = new[]
            {
                new PolicySectionRule { SectionKind = PromptSectionKind.UserIntent, Required = true, MaxItems = 1, Order = 1 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessContext, Required = true, MaxItems = 15, Order = 2 },
                new PolicySectionRule { SectionKind = PromptSectionKind.SemanticPaths, Required = true, MaxItems = 15, MaxTokens = 2000, Order = 3 },
                new PolicySectionRule { SectionKind = PromptSectionKind.EntitiesTables, Required = true, MaxItems = 20, Order = 4 },
                new PolicySectionRule { SectionKind = PromptSectionKind.ImportantMethods, Required = true, MaxItems = 20, MaxTokens = 2500, Order = 5 },
                new PolicySectionRule { SectionKind = PromptSectionKind.BusinessRules, Required = false, MaxItems = 15, Order = 6 },
                new PolicySectionRule { SectionKind = PromptSectionKind.RelevantRoutes, Required = false, MaxItems = 10, Order = 7 },
                new PolicySectionRule { SectionKind = PromptSectionKind.MissingInformation, Required = true, MaxItems = 10, MaxTokens = 800, Order = 8 },
                new PolicySectionRule { SectionKind = PromptSectionKind.Summary, Required = true, MaxItems = 1, Order = 9 }
            }
        };
    }
}

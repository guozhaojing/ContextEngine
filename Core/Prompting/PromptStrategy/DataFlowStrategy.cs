// =============================================================================
// PromptStrategy/DataFlowStrategy.cs — strategy for data/schema analysis
// =============================================================================

using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;
using Core.Prompting.PromptTemplates;
using Core.QueryUnderstanding;

namespace Core.Prompting.PromptStrategy;

public sealed class DataFlowStrategy : IPromptStrategy
{
    public QueryIntent SupportedIntent => QueryIntent.EntityLookup;
    public string StrategyName => "DataFlow";

    public PromptStrategyResult Execute(StructuredContext context, ContextPolicy policy)
    {
        var template = CreateTemplate();
        var renderer = new TemplateRenderer();
        var executionPlan = renderer.BuildExecutionPlan(context, policy);

        var finalPrompt = new FinalPrompt
        {
            PromptId = $"dataflow-{DateTime.Now:yyyyMMddHHmmss}",
            Query = context.Query,
            IntentSummary = BuildIntentSummary(context),
            Content = renderer.Render(template, context, policy),
            Sections = BuildSections(context),
            Anchors = new CodeAnchors
            {
                Entities = context.Entities.Take(20).ToList(),
                Tables = context.Tables.Take(20).ToList(),
                Methods = context.CompressedMethods.Take(15).Select(FirstLine).ToList(),
                Routes = context.Routes.Take(5).ToList()
            },
            ExecutionPlan = executionPlan,
            ExpectedOutputFormat = "## Entity Map\n...\n\n## Table Schema\n...\n\n## Data Flow\n[API → Service → Repository → Entity → Table]\n\n## NHibernate Mappings\n...",
            Constraints = Array.Empty<string>(),
            TokenEstimate = ContextBudgetEstimator.Estimate(executionPlan),
            StrategyName = StrategyName,
            TemplateName = template.TemplateName,
            PolicyName = policy.PolicyName,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["focus"] = "repositories, entity_access, tables, nh_paths",
                ["priority_sections"] = "EntitiesTables, SemanticPaths, ImportantMethods"
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
                ["primary_focus"] = "entity_table_mapping",
                ["secondary_focus"] = "data_access_paths",
                ["section_emphasis"] = "entities_and_paths"
            }
        };
    }

    private static PromptTemplate CreateTemplate()
    {
        return new PromptTemplate
        {
            TemplateId = "dataflow-v1",
            TemplateName = "Data Flow Template",
            Description = "Template for data/schema analysis",
            SystemInstruction = "You are analyzing data flow in a codebase. Map entities to database tables, trace NHibernate access paths, identify repositories and their entity types, and understand how data moves from API entry points through services to the database layer.",
            Slots = new[]
            {
                new TemplateSlot { SlotName = "query_intent", SlotTitle = "Data Query & Intent", Placeholder = "{query_intent}", Required = true },
                new TemplateSlot { SlotName = "entities_tables", SlotTitle = "Entities & Tables Mapping", Placeholder = "{entities_tables}", Required = true },
                new TemplateSlot { SlotName = "semantic_paths", SlotTitle = "Data Access Paths (API → DB)", Placeholder = "{semantic_paths}", Required = true },
                new TemplateSlot { SlotName = "methods", SlotTitle = "Repository & Data Access Methods", Placeholder = "{methods}", Required = true },
                new TemplateSlot { SlotName = "business_context", SlotTitle = "Data Context", Placeholder = "{business_context}", Required = true },
                new TemplateSlot { SlotName = "missing_info", SlotTitle = "Missing Context & Unmapped Entities", Placeholder = "{missing_info}", Required = true }
            },
            OutputFormat = "Provide your analysis in the following format:\n\n## Entity Map\n[Entity class → Database table mappings]\n\n## Table Schema\n[Table descriptions and key columns]\n\n## Data Flow\n[Complete API → Entity → Table flow for each path]\n\n## NHibernate Mappings\n[.hbm.xml or annotation-based mappings found]"
        };
    }

    private static IReadOnlyList<PromptSection> BuildSections(StructuredContext context)
    {
        var sections = new List<PromptSection>
        {
            BuildSection("user-intent", "Data Query", PromptSectionKind.UserIntent, $"Query: {context.Query}\nIntent: {context.Intent}", 10)
        };

        if (context.Entities.Count > 0 || context.Tables.Count > 0)
            sections.Add(BuildSection("entities", "Entity ↔ Table Mappings", PromptSectionKind.EntitiesTables,
                $"Entities ({context.Entities.Count}): {string.Join(", ", context.Entities.Take(20))}\n" +
                $"Tables ({context.Tables.Count}): {string.Join(", ", context.Tables.Take(20))}", 9));

        if (context.SemanticPaths.Count > 0)
            sections.Add(BuildSection("paths", "Data Access Paths", PromptSectionKind.SemanticPaths,
                string.Join('\n', context.SemanticPaths.Take(20)), 8));

        if (context.CompressedMethods.Count > 0)
            sections.Add(BuildSection("methods", "Repository Methods", PromptSectionKind.ImportantMethods,
                string.Join('\n', context.CompressedMethods.Take(15).Select(FirstLine)), 7));

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
        return $"Data flow analysis for: {context.Query}\n" +
               $"Focus: entity/table mapping, data access paths, repositories.\n" +
               $"Retrieved {context.Entities.Count} entities, {context.Tables.Count} tables, {context.SemanticPaths.Count} data paths.";
    }

    private static string FirstLine(string text)
    {
        var idx = text.IndexOf('\n');
        return idx >= 0 ? text[..idx].Trim() : text.Trim();
    }
}

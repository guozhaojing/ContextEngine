// =============================================================================
// Prioritization/QueryFocusAnalyzer.cs — maps query intent to section focus areas
// =============================================================================

using Core.Graph;
using Core.QueryUnderstanding;

namespace Core.Prompting.Prioritization;

public sealed class QueryFocusAnalyzer
{
    private readonly GraphQueryService? _queryService;

    public QueryFocusAnalyzer(GraphQueryService? queryService = null)
    {
        _queryService = queryService;
    }

    public QueryFocus Analyze(string query, QueryIntent intent)
    {
        var primaryCategory = intent switch
        {
            QueryIntent.ValidationLookup => "validation",
            QueryIntent.FlowAnalysis => "bug",
            QueryIntent.EntityLookup => "data",
            QueryIntent.ImpactAnalysis => "refactor",
            QueryIntent.RouteLookup => "feature",
            _ => InferCategoryFromQuery(query)
        };

        var focusAreas = GetFocusAreas(primaryCategory);
        var sectionWeights = GetSectionWeights(primaryCategory);

        return new QueryFocus
        {
            PrimaryCategory = primaryCategory,
            Query = query,
            Intent = intent,
            FocusAreas = focusAreas,
            SectionWeights = sectionWeights
        };
    }

    private static string InferCategoryFromQuery(string query)
    {
        var lower = query.ToLowerInvariant();

        if (lower.Contains("bug") || lower.Contains("error") || lower.Contains("exception") ||
            lower.Contains("fix") || lower.Contains("broken") || lower.Contains("crash") ||
            lower.Contains("故障") || lower.Contains("异常") || lower.Contains("错误"))
            return "bug";

        if (lower.Contains("feature") || lower.Contains("add") || lower.Contains("new") ||
            lower.Contains("create") || lower.Contains("implement") ||
            lower.Contains("新增") || lower.Contains("添加") || lower.Contains("创建"))
            return "feature";

        if (lower.Contains("refactor") || lower.Contains("restructure") || lower.Contains("clean") ||
            lower.Contains("重") || lower.Contains("优化"))
            return "refactor";

        if (lower.Contains("sql") || lower.Contains("data") || lower.Contains("table") ||
            lower.Contains("query") || lower.Contains("数据"))
            return "data";

        return "general";
    }

    private static IReadOnlyList<string> GetFocusAreas(string category)
    {
        return category switch
        {
            "bug" => new[] { "validation", "exception_flow", "stack_path", "guard_clauses" },
            "feature" => new[] { "routes", "services", "entities", "orchestration" },
            "refactor" => new[] { "fan_out", "dependencies", "shared_services" },
            "data" => new[] { "repositories", "entity_access", "tables", "nh_paths" },
            "validation" => new[] { "rules", "constraints", "guards", "permissions" },
            _ => new[] { "routes", "entities", "methods", "rules" }
        };
    }

    private static Dictionary<string, double> GetSectionWeights(string category)
    {
        return category switch
        {
            "bug" => new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["BusinessRules"] = 0.95,
                ["Constraints"] = 0.90,
                ["ImportantMethods"] = 0.85,
                ["SemanticPaths"] = 0.80,
                ["UserIntent"] = 0.75,
                ["BusinessContext"] = 0.70,
                ["EntitiesTables"] = 0.60,
                ["MissingInformation"] = 0.55,
                ["RelevantRoutes"] = 0.50,
                ["Summary"] = 0.40
            },
            "feature" => new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["RelevantRoutes"] = 0.95,
                ["BusinessContext"] = 0.90,
                ["ImportantMethods"] = 0.85,
                ["EntitiesTables"] = 0.80,
                ["UserIntent"] = 0.75,
                ["SemanticPaths"] = 0.70,
                ["BusinessRules"] = 0.60,
                ["MissingInformation"] = 0.55,
                ["Constraints"] = 0.50,
                ["Summary"] = 0.40
            },
            "refactor" => new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["ImportantMethods"] = 0.95,
                ["SemanticPaths"] = 0.90,
                ["EntitiesTables"] = 0.85,
                ["BusinessContext"] = 0.80,
                ["UserIntent"] = 0.75,
                ["RelevantRoutes"] = 0.70,
                ["BusinessRules"] = 0.60,
                ["MissingInformation"] = 0.55,
                ["Constraints"] = 0.50,
                ["Summary"] = 0.40
            },
            "data" => new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["EntitiesTables"] = 0.95,
                ["SemanticPaths"] = 0.90,
                ["ImportantMethods"] = 0.85,
                ["BusinessContext"] = 0.80,
                ["UserIntent"] = 0.75,
                ["BusinessRules"] = 0.70,
                ["MissingInformation"] = 0.65,
                ["RelevantRoutes"] = 0.55,
                ["Constraints"] = 0.50,
                ["Summary"] = 0.40
            },
            _ => new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["UserIntent"] = 0.95,
                ["BusinessContext"] = 0.90,
                ["SemanticPaths"] = 0.85,
                ["EntitiesTables"] = 0.80,
                ["ImportantMethods"] = 0.75,
                ["BusinessRules"] = 0.70,
                ["RelevantRoutes"] = 0.65,
                ["MissingInformation"] = 0.60,
                ["Constraints"] = 0.55,
                ["Summary"] = 0.40
            }
        };
    }
}

public sealed class QueryFocus
{
    public string PrimaryCategory { get; init; } = "general";
    public string Query { get; init; } = "";
    public QueryIntent Intent { get; init; }
    public IReadOnlyList<string> FocusAreas { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, double> SectionWeights { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
}

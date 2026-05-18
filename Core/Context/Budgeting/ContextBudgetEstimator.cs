// =============================================================================
// Budgeting/ContextBudgetEstimator.cs — token estimation for context assembly
// =============================================================================

using Core.Retrieval.Embedding;

namespace Core.Context.Budgeting;

public static class ContextBudgetEstimator
{
    public const int OverheadPerPath = 15;
    public const int OverheadPerRoute = 20;
    public const int OverheadPerEntity = 8;
    public const int OverheadPerRule = 12;
    public const int OverheadPerCompressedMethod = 10;
    public const int OverheadPerSection = 25;

    public static int Estimate(string content)
    {
        return TokenEstimator.Estimate(content);
    }

    public static int EstimateList(IEnumerable<string> items, int overheadPerItem)
    {
        var total = 0;
        foreach (var item in items)
            total += Estimate(item) + overheadPerItem;
        return total;
    }

    public static int EstimateContext(Models.StructuredContext context)
    {
        var tokens = 0;
        tokens += Estimate(context.Query);
        tokens += Estimate(context.Intent);
        tokens += Estimate(context.Summary);
        tokens += EstimateList(context.SemanticPaths, OverheadPerPath);
        tokens += EstimateList(context.Routes, OverheadPerRoute);
        tokens += EstimateList(context.Entities, OverheadPerEntity);
        tokens += EstimateList(context.Tables, OverheadPerEntity);
        tokens += EstimateList(context.BusinessRules, OverheadPerRule);
        tokens += EstimateList(context.CompressedMethods, OverheadPerCompressedMethod);
        tokens += 50;

        return tokens;
    }

    public static int EstimateSection(ContextSection section)
    {
        return Estimate(section.Title) + Estimate(section.Content) + OverheadPerSection;
    }
}

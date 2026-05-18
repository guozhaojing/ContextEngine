// =============================================================================
// Systems/StrategyCoverageValidator.cs — validates strategy intent coverage
// =============================================================================

using Core.Prompting.PromptStrategy;
using Core.QueryUnderstanding;

namespace Core.Evaluation.Systems;

public sealed class StrategyCoverageValidator
{
    private readonly Dictionary<QueryIntent, IPromptStrategy> _strategies;

    public StrategyCoverageValidator()
    {
        _strategies = new Dictionary<QueryIntent, IPromptStrategy>
        {
            [QueryIntent.FlowAnalysis] = new BugFixStrategy(),
            [QueryIntent.RouteLookup] = new FeatureImplementationStrategy(),
            [QueryIntent.ImpactAnalysis] = new RefactorStrategy(),
            [QueryIntent.EntityLookup] = new DataFlowStrategy(),
            [QueryIntent.ValidationLookup] = new ValidationStrategy()
        };
    }

    public StrategyCoverageReport Validate()
    {
        var allIntents = Enum.GetValues<QueryIntent>();
        var entries = new List<StrategyCoverageEntry>();

        foreach (var intent in allIntents)
        {
            if (intent == QueryIntent.Unknown) continue;

            var covered = _strategies.ContainsKey(intent);
            var strategyName = covered ? _strategies[intent].StrategyName : "NONE";

            entries.Add(new StrategyCoverageEntry
            {
                Intent = intent.ToString(),
                HasStrategy = covered,
                StrategyName = strategyName,
                Status = covered ? "covered" : "missing"
            });
        }

        var coverageRatio = (double)entries.Count(e => e.HasStrategy) / entries.Count;

        return new StrategyCoverageReport
        {
            ReportId = $"strategy-coverage-{DateTime.Now:yyyyMMddHHmmss}",
            Entries = entries,
            TotalIntents = entries.Count,
            CoveredIntents = entries.Count(e => e.HasStrategy),
            MissingIntents = entries.Count(e => !e.HasStrategy),
            CoverageRatio = Math.Round(coverageRatio, 3),
            IsFullyCovered = entries.All(e => e.HasStrategy),
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }
}

public sealed class StrategyCoverageReport
{
    public required string ReportId { get; init; }
    public required IReadOnlyList<StrategyCoverageEntry> Entries { get; init; }
    public int TotalIntents { get; init; }
    public int CoveredIntents { get; init; }
    public int MissingIntents { get; init; }
    public double CoverageRatio { get; init; }
    public bool IsFullyCovered { get; init; }
    public string GeneratedAt { get; init; } = "";
}

public sealed class StrategyCoverageEntry
{
    public required string Intent { get; init; }
    public bool HasStrategy { get; init; }
    public string StrategyName { get; init; } = "";
    public string Status { get; init; } = "unknown";
}

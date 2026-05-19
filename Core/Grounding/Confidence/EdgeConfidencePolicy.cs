// =============================================================================
// Grounding/Confidence/EdgeConfidencePolicy.cs — deterministic decay rules
// =============================================================================
// Determinism: all decay factors are compile-time constants. No learned weights,
//   no runtime heuristics, no hidden adjustments.
// Provenance: each propagation records which edge type and decay factor was applied.
// Replay: EdgeConfidencePolicy is stateless; identical edge type → identical factor.
// Grounding: edge types map directly to fixed decay multipliers.
// =============================================================================

using System.Collections.Frozen;

namespace Core.Grounding.Confidence;

public sealed class EdgeConfidencePolicy
{
    private readonly FrozenDictionary<string, double> _decayFactors;
    private readonly FrozenDictionary<string, string> _edgeJustifications;

    public EdgeConfidencePolicy(EdgeConfidencePolicyOptions? options = null)
    {
        var opts = options ?? EdgeConfidencePolicyOptions.Default;
        _decayFactors = opts.DecayFactors.ToFrozenDictionary(StringComparer.Ordinal);
        _edgeJustifications = opts.EdgeJustifications.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public double GetDecayFactor(string edgeKind)
    {
        return _decayFactors.TryGetValue(edgeKind, out var factor)
            ? factor
            : EdgeConfidencePolicyConstants.DefaultDecay;
    }

    public string GetJustification(string edgeKind)
    {
        return _edgeJustifications.TryGetValue(edgeKind, out var justification)
            ? justification
            : "Unknown edge type: default decay applied.";
    }

    public GroundingConfidence Propagate(GroundingConfidence sourceConfidence, string edgeKind)
    {
        var decayFactor = GetDecayFactor(edgeKind);
        var newScore = sourceConfidence.Score * decayFactor;
        var newHopDistance = sourceConfidence.HopDistance + 1;
        var hasSpeculativeAncestor = sourceConfidence.HasSpeculativeAncestor
            || sourceConfidence.Level >= ConfidenceLevel.Speculative
            || decayFactor < EdgeConfidencePolicyConstants.SpeculativeThreshold;

        return new GroundingConfidence(newScore, newHopDistance, hasSpeculativeAncestor);
    }

    public IReadOnlyList<EdgeConfidenceRule> GetAllRules()
    {
        return _decayFactors
            .Select(kvp => new EdgeConfidenceRule
            {
                EdgeKind = kvp.Key,
                DecayFactor = kvp.Value,
                Justification = GetJustification(kvp.Key),
            })
            .OrderByDescending(r => r.DecayFactor)
            .ThenBy(r => r.EdgeKind, StringComparer.Ordinal)
            .ToList();
    }

    public static readonly EdgeConfidencePolicy Default = new();
}

public sealed class EdgeConfidenceRule
{
    public required string EdgeKind { get; init; }
    public double DecayFactor { get; init; }
    public required string Justification { get; init; }
}

public sealed class EdgeConfidencePolicyOptions
{
    public required IReadOnlyDictionary<string, double> DecayFactors { get; init; }
    public required IReadOnlyDictionary<string, string> EdgeJustifications { get; init; }

    public static EdgeConfidencePolicyOptions Default => new()
    {
        DecayFactors = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [EdgeDecayKind.DirectSymbolBinding] = EdgeConfidencePolicyConstants.DirectSymbolBinding,
            [EdgeDecayKind.ExplicitInvocation] = EdgeConfidencePolicyConstants.ExplicitInvocation,
            [EdgeDecayKind.ControlFlow] = EdgeConfidencePolicyConstants.ControlFlow,
            [EdgeDecayKind.DataFlow] = EdgeConfidencePolicyConstants.DataFlow,
            [EdgeDecayKind.Inheritance] = EdgeConfidencePolicyConstants.Inheritance,
            [EdgeDecayKind.ConfigurationBinding] = EdgeConfidencePolicyConstants.ConfigurationBinding,
            [EdgeDecayKind.SemanticSimilarity] = EdgeConfidencePolicyConstants.SemanticSimilarity,
            [EdgeDecayKind.PropagationInference] = EdgeConfidencePolicyConstants.PropagationInference,
            [EdgeDecayKind.SpeculativeExpansion] = EdgeConfidencePolicyConstants.SpeculativeExpansion,
        },
        EdgeJustifications = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EdgeDecayKind.DirectSymbolBinding] = "Direct Roslyn symbol binding — full confidence.",
            [EdgeDecayKind.ExplicitInvocation] = "Explicit method invocation in syntax tree.",
            [EdgeDecayKind.ControlFlow] = "Control flow path — minor uncertainty from branching.",
            [EdgeDecayKind.DataFlow] = "Data flow through variables/parameters — slight loss.",
            [EdgeDecayKind.Inheritance] = "Virtual dispatch or interface resolution — minor loss.",
            [EdgeDecayKind.ConfigurationBinding] = "Configuration-driven binding (DI, XML) — moderate loss.",
            [EdgeDecayKind.SemanticSimilarity] = "Semantic heuristics (name matching, pattern) — significant loss.",
            [EdgeDecayKind.PropagationInference] = "Multi-hop propagation inference — substantial loss.",
            [EdgeDecayKind.SpeculativeExpansion] = "Speculative graph expansion — minimal confidence, not grounded.",
        },
    };
}

public static class EdgeConfidencePolicyConstants
{
    public const double DirectSymbolBinding = 1.00;
    public const double ExplicitInvocation = 0.95;
    public const double ControlFlow = 0.92;
    public const double DataFlow = 0.90;
    public const double Inheritance = 0.88;
    public const double ConfigurationBinding = 0.85;
    public const double SemanticSimilarity = 0.75;
    public const double PropagationInference = 0.60;
    public const double SpeculativeExpansion = 0.40;
    public const double DefaultDecay = 0.70;
    public const double SpeculativeThreshold = 0.50;
}

public static class EdgeDecayKind
{
    public const string DirectSymbolBinding = "direct-symbol-binding";
    public const string ExplicitInvocation = "explicit-invocation";
    public const string ControlFlow = "control-flow";
    public const string DataFlow = "data-flow";
    public const string Inheritance = "inheritance";
    public const string ConfigurationBinding = "configuration-binding";
    public const string SemanticSimilarity = "semantic-similarity";
    public const string PropagationInference = "propagation-inference";
    public const string SpeculativeExpansion = "speculative-expansion";
}

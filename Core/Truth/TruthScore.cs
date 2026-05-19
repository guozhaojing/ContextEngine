using Core.Graph.Analysis;

// =============================================================================
// Truth/TruthScore.cs — unified truth/confidence score for all graph elements
// =============================================================================
// Every edge, entity, and context section receives a TruthScore that captures:
//   - Source reliability (Roslyn > NH > Spring > inferred)
//   - Evidence strength (semantic > syntax > pattern)
//   - Grounding status (grounded vs. ungrounded)
//   - Propagation depth (how far from source evidence)
// =============================================================================

namespace Core.Truth;

public readonly struct TruthScore : IEquatable<TruthScore>, IComparable<TruthScore>
{
    public TruthScore(
        double value,
        EvidenceStrength evidence,
        TruthSource source,
        int propagationDepth = 0,
        bool isGrounded = true)
    {
        Value = Math.Clamp(value, 0.0, 1.0);
        Evidence = evidence;
        Source = source;
        PropagationDepth = Math.Max(0, propagationDepth);
        IsGrounded = isGrounded;
    }

    public double Value { get; }

    public EvidenceStrength Evidence { get; }

    public TruthSource Source { get; }

    public int PropagationDepth { get; }

    public bool IsGrounded { get; }

    public bool IsFact => Evidence >= EvidenceStrength.SemanticDirect && IsGrounded;

    public bool IsInferred => Evidence < EvidenceStrength.SyntaxDirect;

    public bool IsUncertain => Value < 0.5;

    public bool IsHallucinated => !IsGrounded && Value < 0.3;

    public bool CanEnterContext => Value >= 0.5 && IsGrounded;

    public bool CanPropagate => Value >= 0.4 && IsGrounded;

    public TruthType Classify()
    {
        if (!IsGrounded) return TruthType.Hallucinated;
        if (IsFact) return TruthType.Fact;
        if (IsInferred) return TruthType.Inferred;
        return TruthType.Uncertain;
    }

    public TruthScore Decay(int steps)
    {
        if (steps <= 0) return this;
        var factor = Math.Pow(0.7, steps);
        return new TruthScore(
            Value * factor,
            Evidence,
            Source,
            PropagationDepth + steps,
            IsGrounded && factor > 0.3
        );
    }

    public static TruthScore Exact(EvidenceStrength evidence = EvidenceStrength.SemanticDirect)
        => new(1.0, evidence, TruthSource.Roslyn, 0, true);

    public static TruthScore High(EvidenceStrength evidence, TruthSource source)
        => new(0.85, evidence, source, 0, true);

    public static TruthScore Medium(EvidenceStrength evidence, TruthSource source)
        => new(0.6, evidence, source, 0, true);

    public static TruthScore Low(EvidenceStrength evidence, TruthSource source, bool grounded = false)
        => new(0.3, evidence, source, 0, grounded);

    public static TruthScore Ungrounded()
        => new(0.0, EvidenceStrength.None, TruthSource.Unknown, 0, false);

    public static TruthScore FromConfidence(ResolutionConfidence confidence, TruthSource source)
        => confidence switch
        {
            ResolutionConfidence.Exact => new(1.0, EvidenceStrength.SemanticDirect, source, 0, true),
            ResolutionConfidence.High => new(0.85, EvidenceStrength.SemanticInferred, source, 0, true),
            ResolutionConfidence.Medium => new(0.6, EvidenceStrength.SyntaxDirect, source, 0, true),
            ResolutionConfidence.Low => new(0.3, EvidenceStrength.SyntaxPattern, source, 0, true),
            _ => Ungrounded()
        };

    public bool Equals(TruthScore other) =>
        Math.Abs(Value - other.Value) < 0.001
        && Evidence == other.Evidence
        && Source == other.Source
        && PropagationDepth == other.PropagationDepth
        && IsGrounded == other.IsGrounded;

    public override bool Equals(object? obj) => obj is TruthScore other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, Evidence, Source, PropagationDepth, IsGrounded);

    public int CompareTo(TruthScore other) => Value.CompareTo(other.Value);

    public override string ToString() =>
        $"[{Source}/{Evidence}] {Value:F2} depth={PropagationDepth} grounded={IsGrounded}";

    public static bool operator ==(TruthScore left, TruthScore right) => left.Equals(right);

    public static bool operator !=(TruthScore left, TruthScore right) => !left.Equals(right);

    public static bool operator >(TruthScore left, TruthScore right) => left.Value > right.Value;

    public static bool operator <(TruthScore left, TruthScore right) => left.Value < right.Value;
}

public enum EvidenceStrength
{
    None = 0,
    SyntaxPattern = 1,
    SyntaxDirect = 2,
    SemanticInferred = 3,
    SemanticDirect = 4,
}

public enum TruthSource
{
    Unknown = 0,
    Roslyn = 1,
    NHibernate = 2,
    SpringNet = 3,
    AnalyzerInferred = 4,
    LLMCompletion = 5,
    Heuristic = 6,
}

public enum TruthType
{
    Fact = 0,
    Inferred = 1,
    Uncertain = 2,
    Hallucinated = 3,
}

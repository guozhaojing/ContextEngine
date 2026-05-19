// =============================================================================
// Grounding/Confidence/GroundingConfidence.cs — confidence level + value type
// =============================================================================
// Determinism: pure score-threshold mapping; same score → same level always.
// Provenance: confidence carries hop distance and speculative ancestry for audit.
// Replay: GroundingConfidence implements IEquatable for regression comparison.
// Grounding: distinguishes Certain/Strong/Moderate/Weak/Speculative/Unsupported.
// =============================================================================

namespace Core.Grounding.Confidence;

public enum ConfidenceLevel
{
    Certain = 0,
    Strong = 1,
    Moderate = 2,
    Weak = 3,
    Speculative = 4,
    Unsupported = 5,
}

public readonly record struct GroundingConfidence : IComparable<GroundingConfidence>
{
    public GroundingConfidence(
        double score,
        int hopDistance = 0,
        bool hasSpeculativeAncestor = false)
    {
        Score = Math.Clamp(score, 0.0, 1.0);
        HopDistance = Math.Max(0, hopDistance);
        HasSpeculativeAncestor = hasSpeculativeAncestor;
        Level = DeriveLevel(Score, HasSpeculativeAncestor);
    }

    public double Score { get; }
    public ConfidenceLevel Level { get; }
    public int HopDistance { get; }
    public bool HasSpeculativeAncestor { get; }

    public bool IsCertain => Level == ConfidenceLevel.Certain;
    public bool IsStrong => Level <= ConfidenceLevel.Strong;
    public bool IsModerate => Level <= ConfidenceLevel.Moderate;
    public bool IsWeak => Level <= ConfidenceLevel.Weak;
    public bool IsSpeculative => Level <= ConfidenceLevel.Speculative;
    public bool IsUnsupported => Level == ConfidenceLevel.Unsupported;

    public bool AllowsAssertiveLanguage => Level <= ConfidenceLevel.Strong;
    public bool AllowsHedgedLanguage => Level <= ConfidenceLevel.Moderate;
    public bool RequiresQualification => Level >= ConfidenceLevel.Weak;
    public bool ShouldSuppressGeneration => Level >= ConfidenceLevel.Unsupported;

    public int CompareTo(GroundingConfidence other)
    {
        var cmp = Score.CompareTo(other.Score);
        if (cmp != 0) return cmp;
        cmp = Level.CompareTo(other.Level);
        if (cmp != 0) return -cmp; // lower level enum = higher confidence
        return -HopDistance.CompareTo(other.HopDistance);
    }

    public override string ToString() =>
        $"[{Level}] {Score:F3} hop={HopDistance} speculativeAncestor={HasSpeculativeAncestor}";

    private static ConfidenceLevel DeriveLevel(double score, bool hasSpeculativeAncestor)
    {
        if (hasSpeculativeAncestor) score = Math.Min(score, 0.55);

        return score switch
        {
            >= 0.95 => ConfidenceLevel.Certain,
            >= 0.80 => ConfidenceLevel.Strong,
            >= 0.60 => ConfidenceLevel.Moderate,
            >= 0.40 => ConfidenceLevel.Weak,
            >= 0.20 => ConfidenceLevel.Speculative,
            _ => ConfidenceLevel.Unsupported,
        };
    }

    public static readonly GroundingConfidence Certain = new(1.0, 0);

    public static readonly GroundingConfidence Unsupported = new(0.0, 0);

    public static GroundingConfidence FromScore(double score, int hopDistance = 0, bool hasSpeculativeAncestor = false)
        => new(score, hopDistance, hasSpeculativeAncestor);
}

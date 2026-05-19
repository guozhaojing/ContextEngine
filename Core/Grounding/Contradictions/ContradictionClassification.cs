// =============================================================================
// Grounding/Contradictions/ContradictionClassification.cs — contradiction taxonomy
// =============================================================================
// Determinism: classification is derived from structured comparison, not ML.
// Provenance: every contradiction pair identifies the conflicting statements.
// Replay: ContradictionFinding is immutable and structurally comparable.
// Grounding: distinguishes 8 contradiction types with deterministic severity.
// =============================================================================

using Core.Runtime;

namespace Core.Grounding.Contradictions;

public enum ContradictionClassification
{
    DirectConflict = 0,
    ConfidenceConflict = 1,
    TemporalConflict = 2,
    SemanticDrift = 3,
    DivergentImplementation = 4,
    ShadowAbstraction = 5,
    StaleGrounding = 6,
    UnsupportedInference = 7,
}

public static class ContradictionSeverity
{
    public static ContradictionSeverityLevel GetSeverity(ContradictionClassification classification)
        => classification switch
        {
            ContradictionClassification.DirectConflict
                or ContradictionClassification.ShadowAbstraction
                or ContradictionClassification.UnsupportedInference
                => ContradictionSeverityLevel.Severe,

            ContradictionClassification.ConfidenceConflict
                or ContradictionClassification.SemanticDrift
                or ContradictionClassification.StaleGrounding
                => ContradictionSeverityLevel.Moderate,

            ContradictionClassification.TemporalConflict
                or ContradictionClassification.DivergentImplementation
                => ContradictionSeverityLevel.Mild,

            _ => ContradictionSeverityLevel.None,
        };

    public static bool ShouldSuppressGeneration(ContradictionSeverityLevel severity)
        => severity >= ContradictionSeverityLevel.Severe;

    public static bool ShouldQualifyGeneration(ContradictionSeverityLevel severity)
        => severity >= ContradictionSeverityLevel.Moderate;
}

public enum ContradictionSeverityLevel
{
    None = 0,
    Mild = 1,
    Moderate = 2,
    Severe = 3,
}

public sealed class ContradictionFinding : IEquatable<ContradictionFinding>
{
    public required string FindingId { get; init; }
    public required ContradictionClassification Classification { get; init; }
    public ContradictionSeverityLevel Severity => ContradictionSeverity.GetSeverity(Classification);

    public required string StatementAId { get; init; }
    public required string StatementBId { get; init; }
    public string StatementAText { get; init; } = "";
    public string StatementBText { get; init; } = "";

    public required string ConflictDescription { get; init; }
    public required IReadOnlyList<string> SharedEvidence { get; init; }
    public required IReadOnlyList<string> DivergentEvidence { get; init; }

    public bool Equals(ContradictionFinding? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(FindingId, other.FindingId)
            && Classification == other.Classification
            && StringComparer.Ordinal.Equals(StatementAId, other.StatementAId)
            && StringComparer.Ordinal.Equals(StatementBId, other.StatementBId)
            && StringComparer.Ordinal.Equals(ConflictDescription, other.ConflictDescription);
    }

    public override bool Equals(object? obj) => obj is ContradictionFinding other && Equals(other);
    public override int GetHashCode() => FindingId.GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        $"[{Classification}/{Severity}] {StatementAId} ↔ {StatementBId}: {ConflictDescription}";
}

public sealed class ContradictionAnalysisResult : IEquatable<ContradictionAnalysisResult>
{
    public required IReadOnlyList<ContradictionFinding> Findings { get; init; }
    public int TotalFindings => Findings.Count;
    public int SevereCount => Findings.Count(f => f.Severity == ContradictionSeverityLevel.Severe);
    public int ModerateCount => Findings.Count(f => f.Severity == ContradictionSeverityLevel.Moderate);
    public int MildCount => Findings.Count(f => f.Severity == ContradictionSeverityLevel.Mild);
    public bool HasSevereConflicts => SevereCount > 0;
    public bool HasConflicts => Findings.Count > 0;

    public static readonly ContradictionAnalysisResult Empty = new()
    {
        Findings = Array.Empty<ContradictionFinding>(),
    };

    public bool Equals(ContradictionAnalysisResult? other)
    {
        if (other is null) return false;
        if (Findings.Count != other.Findings.Count) return false;
        for (var i = 0; i < Findings.Count; i++)
            if (!Findings[i].Equals(other.Findings[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is ContradictionAnalysisResult other && Equals(other);
    public override int GetHashCode() => Findings.Count;
}

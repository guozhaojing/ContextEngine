// =============================================================================
// Runtime/Governance/SemanticTransitionValidator.cs — state transition validation
// =============================================================================
// Determinism: all validation rules are static checks on before/after snapshots.
//   - Same before/after pair → identical TransitionValidationResult every time.
// Provenance: each violation references the specific state change that caused it.
// Replay: TransitionValidationResult implements IEquatable for regression.
// Grounding: enforces confidence monotonicity, contradiction stability, evidence
//   lineage preservation, and fingerprint stability across transitions.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Grounding.Contradictions;

namespace Core.Runtime.Governance;

public sealed class SemanticTransitionValidator
{
    private readonly TransitionValidatorOptions _options;

    public SemanticTransitionValidator(TransitionValidatorOptions? options = null)
    {
        _options = options ?? TransitionValidatorOptions.Default;
    }

    public TransitionValidationResult ValidateTransition(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after)
    {
        var violations = new List<TransitionViolation>();
        var violationId = 0;

        ValidateConfidenceDecay(before, after, violations, ref violationId);
        ValidateContradictionStability(before, after, violations, ref violationId);
        ValidateEvidenceLineagePreservation(before, after, violations, ref violationId);
        ValidateFingerprintStability(before, after, violations, ref violationId);
        ValidateNoConfidenceInflation(before, after, violations, ref violationId);
        ValidateNoSpeculativeEscalationInTransition(before, after, violations, ref violationId);
        ValidateSeverityMonotonicity(before, after, violations, ref violationId);

        return new TransitionValidationResult
        {
            BeforeSnapshotId = before.SnapshotId,
            AfterSnapshotId = after.SnapshotId,
            IsValid = violations.Count == 0,
            Violations = violations
                .OrderBy(v => v.ViolationType)
                .ThenBy(v => v.ViolationId, StringComparer.Ordinal)
                .ToList(),
            ValidatedAt = System.DateTime.UtcNow.ToString("O"),
        };
    }

    private void ValidateConfidenceDecay(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        List<TransitionViolation> violations,
        ref int violationId)
    {
        if (!_options.CheckConfidenceDecay) return;

        var beforeConf = ToStatementConfidenceMap(before);
        var afterConf = ToStatementConfidenceMap(after);

        foreach (var (stmtId, beforeScore) in beforeConf
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!afterConf.TryGetValue(stmtId, out var afterScore)) continue;

            if (afterScore > beforeScore + _options.ConfidenceEpsilon)
            {
                violations.Add(new TransitionViolation
                {
                    ViolationId = $"cd-{violationId++:D5}",
                    ViolationType = TransitionViolationType.ConfidenceInflation,
                    Description = $"Confidence increased from {beforeScore:F4} to {afterScore:F4} for statement '{stmtId}' without new evidence.",
                    AffectedStatementId = stmtId,
                });
            }
        }
    }

    private void ValidateContradictionStability(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        List<TransitionViolation> violations,
        ref int violationId)
    {
        if (!_options.CheckContradictionStability) return;

        var beforeSevere = before.Contradictions.SevereCount;
        var afterSevere = after.Contradictions.SevereCount;

        if (afterSevere > beforeSevere
            && _options.FlagContradictionEscalation)
        {
            violations.Add(new TransitionViolation
            {
                ViolationId = $"cs-{violationId++:D5}",
                ViolationType = TransitionViolationType.ContradictionEscalation,
                Description = $"Severe contradictions increased from {beforeSevere} to {afterSevere} during transition.",
                AffectedStatementId = "",
            });
        }

        var beforeTotal = before.Contradictions.TotalFindings;
        var afterTotal = after.Contradictions.TotalFindings;

        if (afterTotal < beforeTotal
            && _options.FlagContradictionSuppression)
        {
            violations.Add(new TransitionViolation
            {
                ViolationId = $"cs-{violationId++:D5}",
                ViolationType = TransitionViolationType.ContradictionSuppressed,
                Description = $"Contradictions decreased from {beforeTotal} to {afterTotal} — possible silent suppression.",
                AffectedStatementId = "",
            });
        }
    }

    private void ValidateEvidenceLineagePreservation(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        List<TransitionViolation> violations,
        ref int violationId)
    {
        if (!_options.CheckEvidenceLineage) return;

        var beforeEvidenceIds = before.EvidenceChain.Entries
            .Select(e => e.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);

        var afterEvidenceIds = after.EvidenceChain.Entries
            .Select(e => e.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);

        var lostEntries = beforeEvidenceIds
            .Except(afterEvidenceIds, StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();

        if (lostEntries.Count > 0 && _options.FlagEvidenceLoss)
        {
            violations.Add(new TransitionViolation
            {
                ViolationId = $"el-{violationId++:D5}",
                ViolationType = TransitionViolationType.EvidenceLineageBroken,
                Description = $"Evidence entries were lost during transition: {string.Join(", ", lostEntries.Take(5))}.",
                AffectedStatementId = "",
            });
        }
    }

    private void ValidateFingerprintStability(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        List<TransitionViolation> violations,
        ref int violationId)
    {
        if (!_options.CheckFingerprintStability) return;

        if (before.Fingerprint is not null && after.Fingerprint is not null
            && !before.Fingerprint.Equals(after.Fingerprint)
            && before.Statements.Count == after.Statements.Count)
        {
            violations.Add(new TransitionViolation
            {
                ViolationId = $"fs-{violationId++:D5}",
                ViolationType = TransitionViolationType.FingerprintInstability,
                Description = "Replay fingerprint changed despite identical statement count. Possible nondeterministic change.",
                AffectedStatementId = "",
            });
        }
    }

    private void ValidateNoConfidenceInflation(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        List<TransitionViolation> violations,
        ref int violationId)
    {
        if (!_options.CheckConfidenceInflation) return;

        var beforeAvg = before.Statements.Count > 0
            ? before.Statements.Average(s => s.Confidence.Score) : 0;
        var afterAvg = after.Statements.Count > 0
            ? after.Statements.Average(s => s.Confidence.Score) : 0;

        if (afterAvg > beforeAvg + _options.ConfidenceInflationThreshold)
        {
            violations.Add(new TransitionViolation
            {
                ViolationId = $"ci-{violationId++:D5}",
                ViolationType = TransitionViolationType.ConfidenceInflation,
                Description = $"Average confidence increased from {beforeAvg:F4} to {afterAvg:F4} — possible inflation.",
                AffectedStatementId = "",
            });
        }
    }

    private void ValidateNoSpeculativeEscalationInTransition(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        List<TransitionViolation> violations,
        ref int violationId)
    {
        if (!_options.CheckSpeculativeEscalation) return;

        var beforeSpecCount = before.Statements.Count(s =>
            s.Confidence.Level >= ConfidenceLevel.Speculative && !s.IsSuppressed);
        var afterSpecCount = after.Statements.Count(s =>
            s.Confidence.Level >= ConfidenceLevel.Speculative && !s.IsSuppressed);

        var beforeGroundedCount = before.Statements.Count(s =>
            s.Confidence.Level <= ConfidenceLevel.Moderate && !s.IsSuppressed);

        if (afterSpecCount > beforeSpecCount + _options.SpeculativeEscalationThreshold
            && beforeGroundedCount >= afterSpecCount)
        {
            violations.Add(new TransitionViolation
            {
                ViolationId = $"se-{violationId++:D5}",
                ViolationType = TransitionViolationType.SpeculativeEscalation,
                Description = $"Speculative statements increased from {beforeSpecCount} to {afterSpecCount} — speculative escalation.",
                AffectedStatementId = "",
            });
        }
    }

    private void ValidateSeverityMonotonicity(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        List<TransitionViolation> violations,
        ref int violationId)
    {
        if (!_options.CheckSeverityMonotonicity) return;

        if ((int)after.ResponseSeverity < (int)before.ResponseSeverity
            && before.Contradictions.HasConflicts)
        {
            violations.Add(new TransitionViolation
            {
                ViolationId = $"sm-{violationId++:D5}",
                ViolationType = TransitionViolationType.SeverityDowngrade,
                Description = $"Response severity downgraded from {before.ResponseSeverity} to {after.ResponseSeverity} while contradictions still exist.",
                AffectedStatementId = "",
            });
        }
    }

    private static Dictionary<string, double> ToStatementConfidenceMap(SemanticStateSnapshot state)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var stmt in state.Statements)
        {
            if (!stmt.IsSuppressed)
                map[stmt.StatementId] = stmt.Confidence.Score;
        }
        return map;
    }
}

public sealed class TransitionValidatorOptions
{
    public bool CheckConfidenceDecay { get; init; } = true;
    public bool CheckConfidenceInflation { get; init; } = true;
    public bool CheckContradictionStability { get; init; } = true;
    public bool CheckEvidenceLineage { get; init; } = true;
    public bool CheckFingerprintStability { get; init; } = true;
    public bool CheckSpeculativeEscalation { get; init; } = true;
    public bool CheckSeverityMonotonicity { get; init; } = true;

    public bool FlagContradictionEscalation { get; init; } = true;
    public bool FlagContradictionSuppression { get; init; } = true;
    public bool FlagEvidenceLoss { get; init; } = true;

    public double ConfidenceEpsilon { get; init; } = 0.0001;
    public double ConfidenceInflationThreshold { get; init; } = 0.05;
    public int SpeculativeEscalationThreshold { get; init; } = 2;

    public static TransitionValidatorOptions Default => new();
}

public sealed class TransitionValidationResult : IEquatable<TransitionValidationResult>
{
    public required string BeforeSnapshotId { get; init; }
    public required string AfterSnapshotId { get; init; }
    public bool IsValid { get; init; }
    public required IReadOnlyList<TransitionViolation> Violations { get; init; }
    public string ValidatedAt { get; init; } = "";

    public bool Equals(TransitionValidationResult? other)
    {
        if (other is null) return false;
        if (IsValid != other.IsValid) return false;
        if (!StringComparer.Ordinal.Equals(BeforeSnapshotId, other.BeforeSnapshotId)) return false;
        if (!StringComparer.Ordinal.Equals(AfterSnapshotId, other.AfterSnapshotId)) return false;
        if (Violations.Count != other.Violations.Count) return false;
        for (var i = 0; i < Violations.Count; i++)
            if (!Violations[i].Equals(other.Violations[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is TransitionValidationResult other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        BeforeSnapshotId.GetHashCode(StringComparison.Ordinal),
        AfterSnapshotId.GetHashCode(StringComparison.Ordinal));
}

public sealed class TransitionViolation : IEquatable<TransitionViolation>
{
    public required string ViolationId { get; init; }
    public required TransitionViolationType ViolationType { get; init; }
    public required string Description { get; init; }
    public string AffectedStatementId { get; init; } = "";

    public bool Equals(TransitionViolation? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(ViolationId, other.ViolationId)
            && ViolationType == other.ViolationType
            && StringComparer.Ordinal.Equals(Description, other.Description);
    }

    public override bool Equals(object? obj) => obj is TransitionViolation other && Equals(other);
    public override int GetHashCode() => ViolationId.GetHashCode(StringComparison.Ordinal);
}

public enum TransitionViolationType
{
    ConfidenceInflation = 0,
    ContradictionEscalation = 1,
    ContradictionSuppressed = 2,
    EvidenceLineageBroken = 3,
    FingerprintInstability = 4,
    SpeculativeEscalation = 5,
    SeverityDowngrade = 6,
}

// =============================================================================
// Runtime/Governance/RuntimeGovernanceEngine.cs — governance orchestration
// =============================================================================
// Determinism: all governance decisions are pure functions of input state.
//   - Same state snapshot → identical governance result every time.
//   - Entry ordering is stable (by EntryId, StringComparer.Ordinal).
// Provenance: every governance decision is audited with reason and details.
// Replay: GovernanceResult is immutable and structurally comparable.
// Grounding: the engine enforces all registered invariants, validates state
//   transitions, detects drift, and prevents semantic corruption.
// =============================================================================

using Core.Grounding.Contradictions;

namespace Core.Runtime.Governance;

public sealed class RuntimeGovernanceEngine
{
    private readonly SemanticInvariantRegistry _registry;
    private readonly SemanticTransitionValidator _transitionValidator;
    private readonly RuntimeDriftDetector _driftDetector;
    private readonly GovernanceEngineOptions _options;

    public RuntimeGovernanceEngine(
        SemanticInvariantRegistry? registry = null,
        SemanticTransitionValidator? transitionValidator = null,
        RuntimeDriftDetector? driftDetector = null,
        GovernanceEngineOptions? options = null)
    {
        _registry = registry ?? SemanticInvariantRegistry.Default;
        _transitionValidator = transitionValidator ?? new SemanticTransitionValidator();
        _driftDetector = driftDetector ?? new RuntimeDriftDetector();
        _options = options ?? GovernanceEngineOptions.Default;
    }

    public GovernanceResult Govern(SemanticStateSnapshot state)
    {
        var trail = GovernanceAuditTrail.Create($"trail-{System.DateTime.UtcNow:yyyyMMdd-HHmmss}");

        var invariantResult = _registry.CheckAll(state);
        trail = trail.Append(new AuditTrailEntry.InvariantCheckEntry
        {
            EntryId = $"ic-{Guid.NewGuid():N}"[..16],
            Result = invariantResult,
        });

        if (!invariantResult.AllPassed)
        {
            var violations = invariantResult.Results
                .Where(r => !r.Passed)
                .OrderBy(r => r.InvariantName, StringComparer.Ordinal)
                .SelectMany(r => r.Violations)
                .ToList();

            trail = trail.Append(new AuditTrailEntry.GuardedEntry
            {
                EntryId = $"ge-{Guid.NewGuid():N}"[..16],
                Decision = GovernanceDecision.Reject,
                Reason = $"{invariantResult.TotalFailed} invariant(s) violated.",
                Details = violations,
            });

            return new GovernanceResult
            {
                Decision = GovernanceDecision.Reject,
                InvariantResult = invariantResult,
                TransitionResult = null,
                DriftReport = null,
                State = state,
                AuditTrail = trail,
                IsReplayable = false,
            };
        }

        trail = trail.Append(new AuditTrailEntry.GuardedEntry
        {
            EntryId = $"ge-{Guid.NewGuid():N}"[..16],
            Decision = GovernanceDecision.Allow,
            Reason = $"All {invariantResult.TotalChecked} invariants passed.",
            Details = Array.Empty<string>(),
        });

        return new GovernanceResult
        {
            Decision = GovernanceDecision.Allow,
            InvariantResult = invariantResult,
            TransitionResult = null,
            DriftReport = null,
            State = state,
            AuditTrail = trail,
            IsReplayable = true,
        };
    }

    public GovernanceResult GovernTransition(
        SemanticStateSnapshot before,
        SemanticStateSnapshot after,
        SemanticStateSnapshot? baseline = null)
    {
        var trail = GovernanceAuditTrail.Create($"trail-{System.DateTime.UtcNow:yyyyMMdd-HHmmss}");

        var beforeInvariantResult = _registry.CheckAll(before);
        trail = trail.Append(new AuditTrailEntry.InvariantCheckEntry
        {
            EntryId = $"ic-before-{Guid.NewGuid():N}"[..12],
            Result = beforeInvariantResult,
        });

        if (!beforeInvariantResult.AllPassed)
        {
            trail = trail.Append(new AuditTrailEntry.RejectionEntry
            {
                EntryId = $"rj-{Guid.NewGuid():N}"[..16],
                Reason = "Before-state failed invariant checks.",
                Violations = beforeInvariantResult.Results
                    .Where(r => !r.Passed)
                    .SelectMany(r => r.Violations)
                    .ToList(),
            });

            return Reject("Before-state failed invariant checks.", beforeInvariantResult, null, null, before, trail);
        }

        var afterInvariantResult = _registry.CheckAll(after);
        trail = trail.Append(new AuditTrailEntry.InvariantCheckEntry
        {
            EntryId = $"ic-after-{Guid.NewGuid():N}"[..12],
            Result = afterInvariantResult,
        });

        if (!afterInvariantResult.AllPassed)
        {
            trail = trail.Append(new AuditTrailEntry.RejectionEntry
            {
                EntryId = $"rj-{Guid.NewGuid():N}"[..16],
                Reason = "After-state failed invariant checks.",
                Violations = afterInvariantResult.Results
                    .Where(r => !r.Passed)
                    .SelectMany(r => r.Violations)
                    .ToList(),
            });

            return Reject("After-state failed invariant checks.", afterInvariantResult, null, null, after, trail);
        }

        var transitionResult = _transitionValidator.ValidateTransition(before, after);
        trail = trail.Append(new AuditTrailEntry.TransitionEntry
        {
            EntryId = $"tr-{Guid.NewGuid():N}"[..16],
            Result = transitionResult,
        });

        if (!transitionResult.IsValid)
        {
            if (_options.RejectOnInvalidTransition)
            {
                trail = trail.Append(new AuditTrailEntry.RejectionEntry
                {
                    EntryId = $"rj-{Guid.NewGuid():N}"[..16],
                    Reason = "Transition validation failed.",
                    Violations = transitionResult.Violations.Select(v => v.Description).ToList(),
                });

                return Reject("Transition validation failed.", afterInvariantResult, transitionResult, null, after, trail);
            }

            trail = trail.Append(new AuditTrailEntry.GuardedEntry
            {
                EntryId = $"ge-{Guid.NewGuid():N}"[..16],
                Decision = GovernanceDecision.Warn,
                Reason = "Transition has violations but rejection is disabled.",
                Details = transitionResult.Violations.Select(v => v.Description).ToList(),
            });
        }
        else
        {
            trail = trail.Append(new AuditTrailEntry.GuardedEntry
            {
                EntryId = $"ge-{Guid.NewGuid():N}"[..16],
                Decision = GovernanceDecision.Allow,
                Reason = "Transition is valid.",
                Details = Array.Empty<string>(),
            });
        }

        DriftReport? driftReport = null;
        if (_options.CheckDrift && baseline is not null)
        {
            driftReport = _driftDetector.DetectDrift(baseline, after);
            trail = trail.Append(new AuditTrailEntry.DriftEntry
            {
                EntryId = $"dr-{Guid.NewGuid():N}"[..16],
                Report = driftReport,
            });

            if (!driftReport.IsStable && _options.RejectOnDrift)
            {
                trail = trail.Append(new AuditTrailEntry.RejectionEntry
                {
                    EntryId = $"rj-{Guid.NewGuid():N}"[..16],
                    Reason = "Semantic drift detected.",
                    Violations = driftReport.Findings.Select(f => f.Description).ToList(),
                });

                return Reject("Semantic drift detected.", afterInvariantResult, transitionResult, driftReport, after, trail);
            }
        }

        return new GovernanceResult
        {
            Decision = transitionResult.IsValid ? GovernanceDecision.Allow : GovernanceDecision.Warn,
            InvariantResult = afterInvariantResult,
            TransitionResult = transitionResult,
            DriftReport = driftReport,
            State = after,
            AuditTrail = trail,
            IsReplayable = true,
        };
    }

    private static GovernanceResult Reject(
        string reason,
        InvariantCheckResultSet invariantResult,
        TransitionValidationResult? transitionResult,
        DriftReport? driftReport,
        SemanticStateSnapshot state,
        GovernanceAuditTrail trail)
    {
        return new GovernanceResult
        {
            Decision = GovernanceDecision.Reject,
            InvariantResult = invariantResult,
            TransitionResult = transitionResult,
            DriftReport = driftReport,
            State = state,
            AuditTrail = trail,
            IsReplayable = false,
        };
    }
}

public sealed class GovernanceEngineOptions
{
    public bool RejectOnInvalidTransition { get; init; } = true;
    public bool CheckDrift { get; init; } = true;
    public bool RejectOnDrift { get; init; } = false;

    public static GovernanceEngineOptions Default => new();
}

public sealed class GovernanceResult : IEquatable<GovernanceResult>
{
    public GovernanceDecision Decision { get; init; }
    public required InvariantCheckResultSet InvariantResult { get; init; }
    public TransitionValidationResult? TransitionResult { get; init; }
    public DriftReport? DriftReport { get; init; }
    public required SemanticStateSnapshot State { get; init; }
    public required GovernanceAuditTrail AuditTrail { get; init; }
    public bool IsReplayable { get; init; }

    public bool IsAllowed => Decision == GovernanceDecision.Allow;
    public bool IsWarned => Decision == GovernanceDecision.Warn;
    public bool IsRejected => Decision == GovernanceDecision.Reject;

    public bool Equals(GovernanceResult? other)
    {
        if (other is null) return false;
        if (Decision != other.Decision) return false;
        if (IsReplayable != other.IsReplayable) return false;
        if (!InvariantResult.Equals(other.InvariantResult)) return false;
        if (!State.Equals(other.State)) return false;
        if (!AuditTrail.Equals(other.AuditTrail)) return false;

        if (TransitionResult is not null && other.TransitionResult is null) return false;
        if (TransitionResult is null && other.TransitionResult is not null) return false;
        if (TransitionResult is not null && !TransitionResult.Equals(other.TransitionResult)) return false;

        if (DriftReport is not null && other.DriftReport is null) return false;
        if (DriftReport is null && other.DriftReport is not null) return false;
        if (DriftReport is not null && !DriftReport.Equals(other.DriftReport)) return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is GovernanceResult other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Decision, IsReplayable);

    public string GenerateGovernanceReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Runtime Governance Report");
        sb.AppendLine($"Decision: {Decision}");
        sb.AppendLine($"Replayable: {IsReplayable}");
        sb.AppendLine($"State: {State.SnapshotId}");
        sb.AppendLine();

        sb.AppendLine("## Invariant Check Results");
        sb.AppendLine($"  Passed: {InvariantResult.TotalPassed}/{InvariantResult.TotalChecked}");
        foreach (var r in InvariantResult.Results
            .Where(r => !r.Passed)
            .OrderBy(r => r.InvariantName, StringComparer.Ordinal))
        {
            sb.AppendLine($"  FAIL [{r.InvariantName}]");
            foreach (var v in r.Violations.OrderBy(v => v, StringComparer.Ordinal))
                sb.AppendLine($"    - {v}");
        }
        sb.AppendLine();

        if (TransitionResult is not null)
        {
            sb.AppendLine("## Transition Validation");
            sb.AppendLine($"  Valid: {TransitionResult.IsValid}");
            sb.AppendLine($"  {TransitionResult.BeforeSnapshotId} → {TransitionResult.AfterSnapshotId}");
            foreach (var v in TransitionResult.Violations)
                sb.AppendLine($"  - [{v.ViolationType}] {v.Description}");
            sb.AppendLine();
        }

        if (DriftReport is not null)
        {
            sb.AppendLine("## Drift Analysis");
            sb.AppendLine($"  Stable: {DriftReport.IsStable}");
            sb.AppendLine($"  {DriftReport.Summary}");
            foreach (var f in DriftReport.Findings)
                sb.AppendLine($"  {f}");
        }

        return sb.ToString();
    }
}

// =============================================================================
// Runtime/Governance/RuntimeDriftDetector.cs — semantic drift & replay instability
// =============================================================================
// Determinism: all drift metrics are computed from structural snapshot comparison.
//   - Same baseline + current → identical DriftReport every time.
// Provenance: drift findings reference specific state changes and magnitude.
// Replay: DriftReport implements IEquatable for regression comparison.
// Grounding: detects confidence inflation, contradiction escalation, provenance
//   erosion, and replay instability across snapshots.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Grounding.Contradictions;

namespace Core.Runtime.Governance;

public sealed class RuntimeDriftDetector
{
    private readonly DriftDetectorOptions _options;

    public RuntimeDriftDetector(DriftDetectorOptions? options = null)
    {
        _options = options ?? DriftDetectorOptions.Default;
    }

    public DriftReport DetectDrift(
        SemanticStateSnapshot? baseline,
        SemanticStateSnapshot current)
    {
        if (baseline is null)
        {
            return new DriftReport
            {
                BaselineSnapshotId = null,
                CurrentSnapshotId = current.SnapshotId,
                IsStable = true,
                Findings = Array.Empty<DriftFinding>(),
                AnalyzedAt = System.DateTime.UtcNow.ToString("O"),
                Summary = "No baseline snapshot available. Drift cannot be assessed.",
            };
        }

        var findings = new List<DriftFinding>();
        var findId = 0;

        DetectConfidenceInflation(baseline, current, findings, ref findId);
        DetectContradictionEscalation(baseline, current, findings, ref findId);
        DetectProvenanceErosion(baseline, current, findings, ref findId);
        DetectReplayInstability(baseline, current, findings, ref findId);
        DetectStatementFlux(baseline, current, findings, ref findId);

        var isStable = findings.Count == 0;

        return new DriftReport
        {
            BaselineSnapshotId = baseline.SnapshotId,
            CurrentSnapshotId = current.SnapshotId,
            IsStable = isStable,
            Findings = findings
                .OrderBy(f => f.DriftType)
                .ThenBy(f => f.FindingId, StringComparer.Ordinal)
                .ToList(),
            AnalyzedAt = System.DateTime.UtcNow.ToString("O"),
            Summary = isStable
                ? "No semantic drift detected. Runtime is stable."
                : $"{findings.Count} drift finding(s) detected.",
        };
    }

    private void DetectConfidenceInflation(
        SemanticStateSnapshot baseline,
        SemanticStateSnapshot current,
        List<DriftFinding> findings,
        ref int findId)
    {
        if (!_options.CheckConfidenceInflation) return;

        var baselineAvg = baseline.Statements.Count > 0
            ? baseline.Statements.Average(s => s.Confidence.Score) : 0;
        var currentAvg = current.Statements.Count > 0
            ? current.Statements.Average(s => s.Confidence.Score) : 0;

        var delta = currentAvg - baselineAvg;

        if (delta > _options.ConfidenceInflationThreshold)
        {
            findings.Add(new DriftFinding
            {
                FindingId = $"inf-{findId++:D5}",
                DriftType = DriftType.ConfidenceInflation,
                Description = $"Average confidence inflated by {delta:F4} ({baselineAvg:F4} → {currentAvg:F4}).",
                ValueBefore = baselineAvg,
                ValueAfter = currentAvg,
                Delta = delta,
            });
        }
    }

    private void DetectContradictionEscalation(
        SemanticStateSnapshot baseline,
        SemanticStateSnapshot current,
        List<DriftFinding> findings,
        ref int findId)
    {
        if (!_options.CheckContradictionEscalation) return;

        var baselineTotal = baseline.Contradictions.TotalFindings;
        var currentTotal = current.Contradictions.TotalFindings;
        var delta = currentTotal - baselineTotal;

        if (delta > _options.ContradictionEscalationThreshold)
        {
            findings.Add(new DriftFinding
            {
                FindingId = $"esc-{findId++:D5}",
                DriftType = DriftType.ContradictionEscalation,
                Description = $"Contradictions increased by {delta} ({baselineTotal} → {currentTotal}).",
                ValueBefore = baselineTotal,
                ValueAfter = currentTotal,
                Delta = delta,
            });
        }

        var baselineSevere = baseline.Contradictions.SevereCount;
        var currentSevere = current.Contradictions.SevereCount;
        var severeDelta = currentSevere - baselineSevere;

        if (severeDelta > 0)
        {
            findings.Add(new DriftFinding
            {
                FindingId = $"esc-sev-{findId++:D5}",
                DriftType = DriftType.ContradictionEscalation,
                Description = $"Severe contradictions increased by {severeDelta} ({baselineSevere} → {currentSevere}).",
                ValueBefore = baselineSevere,
                ValueAfter = currentSevere,
                Delta = severeDelta,
            });
        }
    }

    private void DetectProvenanceErosion(
        SemanticStateSnapshot baseline,
        SemanticStateSnapshot current,
        List<DriftFinding> findings,
        ref int findId)
    {
        if (!_options.CheckProvenanceErosion) return;

        var baselineEvidence = baseline.EvidenceChain.Count;
        var currentEvidence = current.EvidenceChain.Count;
        var delta = baselineEvidence - currentEvidence;

        if (delta > _options.ProvenanceErosionThreshold)
        {
            findings.Add(new DriftFinding
            {
                FindingId = $"ero-{findId++:D5}",
                DriftType = DriftType.ProvenanceErosion,
                Description = $"Evidence chain reduced by {delta} entries ({baselineEvidence} → {currentEvidence}).",
                ValueBefore = baselineEvidence,
                ValueAfter = currentEvidence,
                Delta = -delta,
            });
        }
    }

    private void DetectReplayInstability(
        SemanticStateSnapshot baseline,
        SemanticStateSnapshot current,
        List<DriftFinding> findings,
        ref int findId)
    {
        if (!_options.CheckReplayInstability) return;

        if (baseline.Fingerprint is not null && current.Fingerprint is not null
            && !baseline.Fingerprint.Equals(current.Fingerprint))
        {
            var stmtCountSame = baseline.Statements.Count == current.Statements.Count;
            findings.Add(new DriftFinding
            {
                FindingId = $"rep-{findId++:D5}",
                DriftType = DriftType.ReplayInstability,
                Description = stmtCountSame
                    ? "Replay fingerprint changed despite identical statement count — possible nondeterminism."
                    : "Replay fingerprint changed — statement count differs between runs.",
                ValueBefore = 0,
                ValueAfter = 0,
                Delta = 0,
            });
        }
    }

    private void DetectStatementFlux(
        SemanticStateSnapshot baseline,
        SemanticStateSnapshot current,
        List<DriftFinding> findings,
        ref int findId)
    {
        if (!_options.CheckStatementFlux) return;

        var baselineIds = baseline.Statements
            .Select(s => s.StatementId)
            .ToHashSet(StringComparer.Ordinal);
        var currentIds = current.Statements
            .Select(s => s.StatementId)
            .ToHashSet(StringComparer.Ordinal);

        var removed = baselineIds
            .Except(currentIds, StringComparer.Ordinal)
            .Count();
        var added = currentIds
            .Except(baselineIds, StringComparer.Ordinal)
            .Count();
        var totalFlux = removed + added;

        if (totalFlux > _options.StatementFluxThreshold)
        {
            findings.Add(new DriftFinding
            {
                FindingId = $"flx-{findId++:D5}",
                DriftType = DriftType.StatementFlux,
                Description = $"Statement flux: {added} added, {removed} removed (total flux: {totalFlux}).",
                ValueBefore = baseline.Statements.Count,
                ValueAfter = current.Statements.Count,
                Delta = current.Statements.Count - baseline.Statements.Count,
            });
        }
    }
}

public sealed class DriftDetectorOptions
{
    public bool CheckConfidenceInflation { get; init; } = true;
    public bool CheckContradictionEscalation { get; init; } = true;
    public bool CheckProvenanceErosion { get; init; } = true;
    public bool CheckReplayInstability { get; init; } = true;
    public bool CheckStatementFlux { get; init; } = true;

    public double ConfidenceInflationThreshold { get; init; } = 0.05;
    public int ContradictionEscalationThreshold { get; init; } = 1;
    public int ProvenanceErosionThreshold { get; init; } = 1;
    public int StatementFluxThreshold { get; init; } = 3;

    public static DriftDetectorOptions Default => new();
}

public sealed class DriftReport : IEquatable<DriftReport>
{
    public string? BaselineSnapshotId { get; init; }
    public required string CurrentSnapshotId { get; init; }
    public bool IsStable { get; init; }
    public required IReadOnlyList<DriftFinding> Findings { get; init; }
    public string AnalyzedAt { get; init; } = "";
    public required string Summary { get; init; }

    public bool Equals(DriftReport? other)
    {
        if (other is null) return false;
        if (IsStable != other.IsStable) return false;
        if (!StringComparer.Ordinal.Equals(Summary, other.Summary)) return false;
        if (Findings.Count != other.Findings.Count) return false;
        for (var i = 0; i < Findings.Count; i++)
            if (!Findings[i].Equals(other.Findings[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is DriftReport other && Equals(other);
    public override int GetHashCode() => IsStable.GetHashCode();
}

public sealed class DriftFinding : IEquatable<DriftFinding>
{
    public required string FindingId { get; init; }
    public required DriftType DriftType { get; init; }
    public required string Description { get; init; }
    public double ValueBefore { get; init; }
    public double ValueAfter { get; init; }
    public double Delta { get; init; }

    public bool Equals(DriftFinding? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(FindingId, other.FindingId)
            && DriftType == other.DriftType
            && StringComparer.Ordinal.Equals(Description, other.Description)
            && Math.Abs(Delta - other.Delta) < 0.0001;
    }

    public override bool Equals(object? obj) => obj is DriftFinding other && Equals(other);
    public override int GetHashCode() => FindingId.GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        $"[{DriftType}] Δ={Delta:F2} {Description}";
}

public enum DriftType
{
    ConfidenceInflation = 0,
    ContradictionEscalation = 1,
    ProvenanceErosion = 2,
    ReplayInstability = 3,
    StatementFlux = 4,
}

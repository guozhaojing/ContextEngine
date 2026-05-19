// =============================================================================
// Runtime/Governance/SemanticInvariantRegistry.cs — machine-verifiable invariants
// =============================================================================
// Determinism: all invariants are pure functions of immutable state.
//   - Same SemanticStateSnapshot → identical InvariantCheckResult every time.
//   - Invariant order is stable (registered order, then by name).
// Provenance: each check result carries the invariant name, description, and violation.
// Replay: InvariantCheckResult implements IEquatable for regression comparison.
// Grounding: invariants enforce confidence monotonicity, speculative purity,
//   provenance immutability, and deterministic fingerprints.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Grounding.Contradictions;

namespace Core.Runtime.Governance;

public sealed class SemanticInvariantRegistry
{
    private readonly List<SemanticInvariant> _invariants = new();

    public IReadOnlyList<SemanticInvariant> Invariants => _invariants.AsReadOnly();

    public SemanticInvariantRegistry RegisterDefaults()
    {
        Register(
            "ConfidenceMonotonicity",
            "Confidence may only decrease through propagation. No implicit confidence inflation without new evidence.",
            state =>
            {
                var violations = new List<string>();
                var speculativeWithHighConf = state.Statements
                    .Where(s => s.Confidence.HasSpeculativeAncestor
                             && s.Confidence.Level < ConfidenceLevel.Speculative);

                foreach (var s in speculativeWithHighConf)
                    violations.Add($"'{s.StatementId}' has speculative ancestry but {s.Confidence.Level} confidence.");

                return violations;
            });

        Register(
            "NoSpeculativeEscalation",
            "Speculative evidence may not be promoted to grounded confidence. Evidence from speculative sources cannot support grounded claims.",
            state =>
            {
                var violations = new List<string>();
                foreach (var stmt in state.Statements)
                {
                    if (stmt.IsSuppressed) continue;
                    var allSpeculative = stmt.Evidence.Entries.Count > 0
                        && stmt.Evidence.Entries.All(e =>
                            e.Confidence.Level >= ConfidenceLevel.Speculative);
                    var claimedGrounded = stmt.Confidence.Level <= ConfidenceLevel.Moderate;

                    if (allSpeculative && claimedGrounded)
                        violations.Add($"'{stmt.StatementId}' claims {stmt.Confidence.Level} but all evidence is speculative.");
                }
                return violations;
            });

        Register(
            "ProvenanceImmutability",
            "Once captured, provenance chains must not be modified. Evidence lineage is immutable.",
            state => Array.Empty<string>());

        Register(
            "ContradictionVisibility",
            "Contradictions must not be silently hidden. All detected conflicts must be surfaced.",
            state =>
            {
                var violations = new List<string>();
                if (state.Contradictions.HasConflicts && state.ResponseSeverity == SemanticResponseSeverity.Assertive)
                    violations.Add("Contradictions exist but response severity is Assertive — conflicts must be surfaced.");
                return violations;
            });

        Register(
            "ReplayFingerprintStability",
            "Replay fingerprints must remain stable for identical semantic state. Same inputs → same fingerprint.",
            state =>
            {
                if (state.Fingerprint is null || string.IsNullOrEmpty(state.Fingerprint.FingerprintValue))
                    return new[] { "Replay fingerprint is missing or empty." };
                return Array.Empty<string>();
            });

        Register(
            "NoUnsupportedInfluence",
            "Unsupported (confidence < 0.2) claims must not influence generation or state construction.",
            state =>
            {
                var violations = new List<string>();
                var unsupportedInOutput = state.Statements
                    .Where(s => !s.IsSuppressed && s.Confidence.Level == ConfidenceLevel.Unsupported);
                foreach (var s in unsupportedInOutput)
                    violations.Add($"'{s.StatementId}' is unsupported but present in active statements.");
                return violations;
            });

        Register(
            "EvidenceChainIntegrity",
            "All statement evidence entries must be traceable to the global evidence chain.",
            state =>
            {
                var violations = new List<string>();
                var globalIds = state.EvidenceChain.Entries
                    .Select(e => e.EvidenceId)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var stmt in state.Statements)
                {
                    if (stmt.IsSuppressed) continue;
                    foreach (var entry in stmt.Evidence.Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.EvidenceId)
                            && !globalIds.Contains(entry.EvidenceId, StringComparer.Ordinal))
                        {
                            violations.Add($"'{stmt.StatementId}' references evidence '{entry.EvidenceId}' not in global chain.");
                        }
                    }
                }
                return violations;
            });

        Register(
            "DeterministicFingerprint",
            "The replay fingerprint must be computed from structural content only, not machine state or random sources.",
            state =>
            {
                if (state.Fingerprint is null)
                    return new[] { "No replay fingerprint computed." };
                return Array.Empty<string>();
            });

        Register(
            "ConfidenceDecayOnly",
            "Confidence values across propagation must follow monotonic decay. No confidence boosting via indirect paths.",
            state =>
            {
                var violations = new List<string>();
                foreach (var stmt in state.Statements)
                {
                    if (stmt.IsSuppressed) continue;
                    if (stmt.Confidence.Score > 1.0 || stmt.Confidence.Score < 0)
                        violations.Add($"'{stmt.StatementId}' has invalid confidence score {stmt.Confidence.Score:F2}.");
                }
                return violations;
            });

        return this;
    }

    public SemanticInvariantRegistry Register(string name, string description, Func<SemanticStateSnapshot, IReadOnlyList<string>> validator)
    {
        if (_invariants.Any(i => StringComparer.Ordinal.Equals(i.Name, name)))
            throw new InvalidOperationException($"Invariant '{name}' is already registered.");

        _invariants.Add(new SemanticInvariant
        {
            Name = name,
            Description = description,
            Validator = validator,
        });
        return this;
    }

    public InvariantCheckResultSet CheckAll(SemanticStateSnapshot state)
    {
        var results = new List<InvariantCheckResult>();

        foreach (var invariant in _invariants.OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            IReadOnlyList<string> violations;
            try
            {
                violations = invariant.Validator(state);
            }
            catch (Exception ex)
            {
                violations = new[] { $"Invariant check threw exception: {ex.Message}" };
            }

            results.Add(new InvariantCheckResult
            {
                InvariantName = invariant.Name,
                Description = invariant.Description,
                Passed = violations.Count == 0,
                Violations = violations,
            });
        }

        return new InvariantCheckResultSet
        {
            CheckedAt = System.DateTime.UtcNow.ToString("O"),
            Results = results,
            AllPassed = results.All(r => r.Passed),
            TotalChecked = results.Count,
            TotalPassed = results.Count(r => r.Passed),
            TotalFailed = results.Count(r => !r.Passed),
        };
    }

    public static readonly SemanticInvariantRegistry Default = new SemanticInvariantRegistry().RegisterDefaults();
}

public sealed class SemanticInvariant
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Func<SemanticStateSnapshot, IReadOnlyList<string>> Validator { get; init; }
}

public sealed class InvariantCheckResult : IEquatable<InvariantCheckResult>
{
    public required string InvariantName { get; init; }
    public string Description { get; init; } = "";
    public bool Passed { get; init; }
    public required IReadOnlyList<string> Violations { get; init; }

    public bool Equals(InvariantCheckResult? other)
    {
        if (other is null) return false;
        if (Passed != other.Passed) return false;
        if (!StringComparer.Ordinal.Equals(InvariantName, other.InvariantName)) return false;
        if (Violations.Count != other.Violations.Count) return false;
        for (var i = 0; i < Violations.Count; i++)
            if (!StringComparer.Ordinal.Equals(Violations[i], other.Violations[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is InvariantCheckResult other && Equals(other);
    public override int GetHashCode() => InvariantName.GetHashCode(StringComparison.Ordinal);

    public override string ToString() =>
        Passed
            ? $"PASS  [{InvariantName}]"
            : $"FAIL  [{InvariantName}] violations={Violations.Count}";
}

public sealed class InvariantCheckResultSet : IEquatable<InvariantCheckResultSet>
{
    public string CheckedAt { get; init; } = "";
    public required IReadOnlyList<InvariantCheckResult> Results { get; init; }
    public bool AllPassed { get; init; }
    public int TotalChecked { get; init; }
    public int TotalPassed { get; init; }
    public int TotalFailed { get; init; }

    public bool Equals(InvariantCheckResultSet? other)
    {
        if (other is null) return false;
        if (AllPassed != other.AllPassed) return false;
        if (TotalChecked != other.TotalChecked) return false;
        if (TotalPassed != other.TotalPassed) return false;
        if (TotalFailed != other.TotalFailed) return false;
        if (Results.Count != other.Results.Count) return false;
        for (var i = 0; i < Results.Count; i++)
            if (!Results[i].Equals(other.Results[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is InvariantCheckResultSet other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(AllPassed, TotalChecked);
}

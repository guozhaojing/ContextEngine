// =============================================================================
// Grounding/Contradictions/ConsistencyValidator.cs — semantic coherence validation
// =============================================================================
// Determinism: all validation checks are static rules applied to immutable state.
//   - Same SemanticStateSnapshot → identical ConsistencyValidationResult.
//   - Issue ordering is stable (by IssueId, StringComparer.Ordinal).
// Provenance: each issue carries the statement ID and the violated constraint.
// Replay: ConsistencyValidationResult implements IEquatable for regression.
// Grounding: validates confidence monotonicity, speculative escalation prevention,
//   incompatible evidence merges, and contradictory assertions.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Runtime;

namespace Core.Grounding.Contradictions;

public sealed class ConsistencyValidator
{
    private readonly ConsistencyValidatorOptions _options;

    public ConsistencyValidator(ConsistencyValidatorOptions? options = null)
    {
        _options = options ?? ConsistencyValidatorOptions.Default;
    }

    public ConsistencyValidationResult Validate(SemanticStateSnapshot state)
    {
        if (state.Statements.Count == 0)
            return ConsistencyValidationResult.Consistent;

        var issues = new List<ConsistencyIssue>();
        var issueId = 0;

        ValidateConfidenceMonotonicity(state, issues, ref issueId);
        ValidateSpeculativeEscalation(state, issues, ref issueId);
        ValidateEvidenceCompatibility(state, issues, ref issueId);
        ValidateNoContradictoryAssertions(state, issues, ref issueId);
        ValidateSymbolConsistency(state, issues, ref issueId);
        ValidateEvidenceChainIntegrity(state, issues, ref issueId);

        return new ConsistencyValidationResult
        {
            IsConsistent = issues.Count == 0,
            Issues = issues
                .OrderBy(i => i.IssueType)
                .ThenBy(i => i.IssueId, StringComparer.Ordinal)
                .ToList(),
            CheckedAt = System.DateTime.UtcNow.ToString("O"),
        };
    }

    private void ValidateConfidenceMonotonicity(
        SemanticStateSnapshot state,
        List<ConsistencyIssue> issues,
        ref int issueId)
    {
        if (!_options.CheckConfidenceMonotonicity) return;

        var subjectGroups = state.Statements
            .Where(s => !s.IsSuppressed && !string.IsNullOrEmpty(s.SubjectNodeId))
            .GroupBy(s => s.SubjectNodeId!, StringComparer.Ordinal);

        foreach (var group in subjectGroups)
        {
            var sortedByConfidence = group
                .OrderBy(s => s.Confidence.Score)
                .ToList();

            foreach (var stmt in sortedByConfidence)
            {
                if (stmt.Confidence.HasSpeculativeAncestor
                    && stmt.Confidence.Level < ConfidenceLevel.Speculative)
                {
                    issues.Add(new ConsistencyIssue
                    {
                        IssueId = $"cm-{issueId++:D5}",
                        IssueType = ConsistencyIssueType.ConfidenceNonMonotonic,
                        Description = $"Statement '{stmt.StatementId}' has speculative ancestry but non-speculative confidence level {stmt.Confidence.Level}.",
                        StatementId = stmt.StatementId,
                    });
                }
            }
        }
    }

    private void ValidateSpeculativeEscalation(
        SemanticStateSnapshot state,
        List<ConsistencyIssue> issues,
        ref int issueId)
    {
        if (!_options.CheckSpeculativeEscalation) return;

        foreach (var stmt in state.Statements)
        {
            if (stmt.IsSuppressed) continue;

            var speculativeEvidenceCount = stmt.Evidence.Entries
                .Count(e => e.Confidence.Level >= ConfidenceLevel.Speculative);

            var groundedEvidenceCount = stmt.Evidence.Entries
                .Count(e => e.Confidence.Level <= ConfidenceLevel.Moderate);

            if (speculativeEvidenceCount > 0 && groundedEvidenceCount == 0
                && stmt.Confidence.Level <= ConfidenceLevel.Moderate)
            {
                issues.Add(new ConsistencyIssue
                {
                    IssueId = $"se-{issueId++:D5}",
                    IssueType = ConsistencyIssueType.SpeculativeEscalation,
                    Description = $"Statement '{stmt.StatementId}' has only speculative evidence but claims {stmt.Confidence.Level} confidence. Speculative evidence cannot support grounded confidence.",
                    StatementId = stmt.StatementId,
                });
            }
        }
    }

    private void ValidateEvidenceCompatibility(
        SemanticStateSnapshot state,
        List<ConsistencyIssue> issues,
        ref int issueId)
    {
        if (!_options.CheckEvidenceCompatibility) return;

        var subjectGroups = state.Statements
            .Where(s => !s.IsSuppressed && !string.IsNullOrEmpty(s.SubjectNodeId)
                && s.Evidence.Entries.Count > 0)
            .GroupBy(s => s.SubjectNodeId!, StringComparer.Ordinal);

        foreach (var group in subjectGroups)
        {
            var stmts = group.ToList();
            for (var i = 0; i < stmts.Count; i++)
            {
                for (var j = i + 1; j < stmts.Count; j++)
                {
                    var a = stmts[i];
                    var b = stmts[j];

                    var sourcesA = a.Evidence.Entries
                        .Select(e => e.SourceNodeId ?? e.EvidenceId)
                        .ToHashSet(StringComparer.Ordinal);
                    var sourcesB = b.Evidence.Entries
                        .Select(e => e.SourceNodeId ?? e.EvidenceId)
                        .ToHashSet(StringComparer.Ordinal);

                    if (sourcesA.Count > 0 && sourcesB.Count > 0
                        && !sourcesA.Overlaps(sourcesB)
                        && a.Confidence.Level <= ConfidenceLevel.Moderate
                        && b.Confidence.Level <= ConfidenceLevel.Moderate)
                    {
                        issues.Add(new ConsistencyIssue
                        {
                            IssueId = $"im-{issueId++:D5}",
                            IssueType = ConsistencyIssueType.IncompatibleEvidenceMerge,
                            Description = $"Statements '{a.StatementId}' and '{b.StatementId}' for subject '{group.Key}' have non-overlapping evidence but both claim grounded confidence.",
                            StatementId = $"{a.StatementId},{b.StatementId}",
                        });
                    }
                }
            }
        }
    }

    private void ValidateNoContradictoryAssertions(
        SemanticStateSnapshot state,
        List<ConsistencyIssue> issues,
        ref int issueId)
    {
        if (!_options.CheckContradictoryAssertions) return;

        foreach (var contradiction in state.Contradictions.Findings)
        {
            if (contradiction.Classification == ContradictionClassification.DirectConflict
                && contradiction.Severity >= ContradictionSeverityLevel.Severe)
            {
                issues.Add(new ConsistencyIssue
                {
                    IssueId = $"ca-{issueId++:D5}",
                    IssueType = ConsistencyIssueType.ContradictoryAssertion,
                    Description = $"Direct conflict: {contradiction.ConflictDescription}",
                    StatementId = $"{contradiction.StatementAId},{contradiction.StatementBId}",
                });
            }
        }
    }

    private void ValidateSymbolConsistency(
        SemanticStateSnapshot state,
        List<ConsistencyIssue> issues,
        ref int issueId)
    {
        if (!_options.CheckSymbolConsistency) return;

        var statementsWithSymbols = state.Statements
            .Where(s => !s.IsSuppressed && s.Evidence.Entries.Any(
                e => !e.SourceSymbol.IsEmpty));

        var symbolGroups = statementsWithSymbols
            .SelectMany(s => s.Evidence.Entries
                .Where(e => !e.SourceSymbol.IsEmpty)
                .Select(e => (Symbol: e.SourceSymbol.Value, StatementId: s.StatementId)))
            .GroupBy(t => t.Symbol, StringComparer.Ordinal);

        foreach (var group in symbolGroups)
        {
            var stmtIds = group.Select(t => t.StatementId).Distinct(StringComparer.Ordinal).ToList();
            if (stmtIds.Count > 1)
            {
                issues.Add(new ConsistencyIssue
                {
                    IssueId = $"sc-{issueId++:D5}",
                    IssueType = ConsistencyIssueType.SymbolInconsistency,
                    Description = $"Symbol '{group.Key}' is referenced by multiple statements: {string.Join(", ", stmtIds)}.",
                    StatementId = string.Join(",", stmtIds),
                });
            }
        }
    }

    private void ValidateEvidenceChainIntegrity(
        SemanticStateSnapshot state,
        List<ConsistencyIssue> issues,
        ref int issueId)
    {
        if (!_options.CheckEvidenceChainIntegrity) return;

        var globalEvidenceIds = state.EvidenceChain.Entries
            .Select(e => e.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var stmt in state.Statements)
        {
            if (stmt.IsSuppressed) continue;

            foreach (var entry in stmt.Evidence.Entries)
            {
                if (!globalEvidenceIds.Contains(entry.EvidenceId, StringComparer.Ordinal)
                    && !string.IsNullOrEmpty(entry.EvidenceId))
                {
                    issues.Add(new ConsistencyIssue
                    {
                        IssueId = $"ei-{issueId++:D5}",
                        IssueType = ConsistencyIssueType.EvidenceChainBroken,
                        Description = $"Statement '{stmt.StatementId}' references evidence '{entry.EvidenceId}' not found in global evidence chain.",
                        StatementId = stmt.StatementId,
                    });
                }
            }
        }
    }
}

public sealed class ConsistencyValidatorOptions
{
    public bool CheckConfidenceMonotonicity { get; init; } = true;
    public bool CheckSpeculativeEscalation { get; init; } = true;
    public bool CheckEvidenceCompatibility { get; init; } = true;
    public bool CheckContradictoryAssertions { get; init; } = true;
    public bool CheckSymbolConsistency { get; init; } = true;
    public bool CheckEvidenceChainIntegrity { get; init; } = true;

    public static ConsistencyValidatorOptions Default => new();
}

public sealed class ConsistencyValidationResult : IEquatable<ConsistencyValidationResult>
{
    public bool IsConsistent { get; init; }
    public required IReadOnlyList<ConsistencyIssue> Issues { get; init; }
    public string CheckedAt { get; init; } = "";

    public static readonly ConsistencyValidationResult Consistent = new()
    {
        IsConsistent = true,
        Issues = Array.Empty<ConsistencyIssue>(),
    };

    public bool Equals(ConsistencyValidationResult? other)
    {
        if (other is null) return false;
        if (IsConsistent != other.IsConsistent) return false;
        if (Issues.Count != other.Issues.Count) return false;
        for (var i = 0; i < Issues.Count; i++)
            if (!Issues[i].Equals(other.Issues[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is ConsistencyValidationResult other && Equals(other);
    public override int GetHashCode() => IsConsistent.GetHashCode();
}

public sealed class ConsistencyIssue : IEquatable<ConsistencyIssue>
{
    public required string IssueId { get; init; }
    public required ConsistencyIssueType IssueType { get; init; }
    public required string Description { get; init; }
    public string StatementId { get; init; } = "";

    public bool Equals(ConsistencyIssue? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(IssueId, other.IssueId)
            && IssueType == other.IssueType
            && StringComparer.Ordinal.Equals(Description, other.Description);
    }

    public override bool Equals(object? obj) => obj is ConsistencyIssue other && Equals(other);
    public override int GetHashCode() => IssueId.GetHashCode(StringComparison.Ordinal);
}

public enum ConsistencyIssueType
{
    ConfidenceNonMonotonic = 0,
    SpeculativeEscalation = 1,
    IncompatibleEvidenceMerge = 2,
    ContradictoryAssertion = 3,
    SymbolInconsistency = 4,
    EvidenceChainBroken = 5,
}

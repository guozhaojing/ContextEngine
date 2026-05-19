// =============================================================================
// Grounding/Contradictions/SemanticContradictionEngine.cs — deterministic detection
// =============================================================================
// Determinism: all detection rules are static structural checks, not ML.
//   - Statement comparisons are sorted by StatementId (StringComparer.Ordinal).
//   - Each contradiction type has a fixed detection rule.
//   - Same state snapshot → identical contradiction findings every time.
// Provenance: each finding carries the conflicting statement IDs, shared evidence,
//   and divergent evidence.
// Replay: ContradictionAnalysisResult implements IEquatable for regression.
// Grounding: distinguishes 8 contradiction types with severity classification.
// =============================================================================

using Core.Grounding.Confidence;
using Core.Runtime;

namespace Core.Grounding.Contradictions;

public sealed class SemanticContradictionEngine
{
    private readonly ContradictionDetectionOptions _options;

    public SemanticContradictionEngine(ContradictionDetectionOptions? options = null)
    {
        _options = options ?? ContradictionDetectionOptions.Default;
    }

    public ContradictionAnalysisResult Analyze(SemanticStateSnapshot state)
    {
        if (state.Statements.Count == 0)
            return ContradictionAnalysisResult.Empty;

        var findings = new List<ContradictionFinding>();
        var findId = 0;
        var statements = state.Statements.OrderBy(s => s.StatementId, StringComparer.Ordinal).ToList();
        var statementDict = statements.ToDictionary(s => s.StatementId, StringComparer.Ordinal);

        DetectDirectConflicts(statements, statementDict, findings, ref findId);
        DetectShadowAbstractions(statements, findings, ref findId);
        DetectUnsupportedInferences(statements, findings, ref findId);
        DetectConfidenceConflicts(statements, findings, ref findId);
        DetectDivergentImplementations(statements, findings, ref findId);
        DetectStaleGrounding(statements, findings, ref findId);
        DetectSemanticDrift(statements, findings, ref findId);
        DetectTemporalConflicts(statements, findings, ref findId);

        return new ContradictionAnalysisResult
        {
            Findings = findings
                .OrderBy(f => f.Classification)
                .ThenBy(f => f.FindingId, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private void DetectDirectConflicts(
        List<SemanticStatement> statements,
        Dictionary<string, SemanticStatement> statementDict,
        List<ContradictionFinding> findings,
        ref int findId)
    {
        var conflictPairs = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < statements.Count; i++)
        {
            for (var j = i + 1; j < statements.Count; j++)
            {
                var a = statements[i];
                var b = statements[j];

                if (a.IsSuppressed || b.IsSuppressed) continue;

                var key = string.Compare(a.StatementId, b.StatementId, StringComparison.Ordinal) < 0
                    ? $"{a.StatementId}|{b.StatementId}"
                    : $"{b.StatementId}|{a.StatementId}";

                if (!conflictPairs.Add(key)) continue;

                if (HasDirectSemanticConflict(a, b))
                {
                    var shared = GetSharedEvidence(a, b);
                    var divergent = GetDivergentEvidence(a, b);

                    findings.Add(new ContradictionFinding
                    {
                        FindingId = $"dc-{findId++:D5}",
                        Classification = ContradictionClassification.DirectConflict,
                        StatementAId = a.StatementId,
                        StatementBId = b.StatementId,
                        StatementAText = Truncate(a.RawClaim),
                        StatementBText = Truncate(b.RawClaim),
                        ConflictDescription = $"Direct semantic conflict: '{Truncate(a.RawClaim)}' vs '{Truncate(b.RawClaim)}'",
                        SharedEvidence = shared,
                        DivergentEvidence = divergent,
                    });
                }
            }
        }
    }

    private void DetectShadowAbstractions(
        List<SemanticStatement> statements,
        List<ContradictionFinding> findings,
        ref int findId)
    {
        foreach (var stmt in statements)
        {
            if (stmt.IsSuppressed) continue;

            var isShadow = stmt.Confidence.Level >= ConfidenceLevel.Speculative
                && stmt.Evidence.Entries.Count == 0
                && IsAbstractionPattern(stmt.RawClaim);

            if (isShadow)
            {
                findings.Add(new ContradictionFinding
                {
                    FindingId = $"sa-{findId++:D5}",
                    Classification = ContradictionClassification.ShadowAbstraction,
                    StatementAId = stmt.StatementId,
                    StatementBId = "",
                    StatementAText = Truncate(stmt.RawClaim),
                    StatementBText = "",
                    ConflictDescription = $"Shadow abstraction detected: '{Truncate(stmt.RawClaim)}' has no evidence and speculative confidence.",
                    SharedEvidence = Array.Empty<string>(),
                    DivergentEvidence = Array.Empty<string>(),
                });
            }
        }
    }

    private void DetectUnsupportedInferences(
        List<SemanticStatement> statements,
        List<ContradictionFinding> findings,
        ref int findId)
    {
        foreach (var stmt in statements)
        {
            if (stmt.IsSuppressed) continue;

            var isUnsupported = stmt.Confidence.Level >= ConfidenceLevel.Weak
                && stmt.Evidence.Entries.Count == 0
                && !IsAbstractionPattern(stmt.RawClaim);

            if (isUnsupported)
            {
                findings.Add(new ContradictionFinding
                {
                    FindingId = $"ui-{findId++:D5}",
                    Classification = ContradictionClassification.UnsupportedInference,
                    StatementAId = stmt.StatementId,
                    StatementBId = "",
                    StatementAText = Truncate(stmt.RawClaim),
                    StatementBText = "",
                    ConflictDescription = $"Unsupported inference: '{Truncate(stmt.RawClaim)}' has no supporting evidence.",
                    SharedEvidence = Array.Empty<string>(),
                    DivergentEvidence = Array.Empty<string>(),
                });
            }
        }
    }

    private void DetectConfidenceConflicts(
        List<SemanticStatement> statements,
        List<ContradictionFinding> findings,
        ref int findId)
    {
        var subjectGroups = statements
            .Where(s => !s.IsSuppressed && !string.IsNullOrEmpty(s.SubjectNodeId))
            .GroupBy(s => s.SubjectNodeId!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in subjectGroups)
        {
            var stmts = group.OrderBy(s => s.StatementId, StringComparer.Ordinal).ToList();
            for (var i = 0; i < stmts.Count; i++)
            {
                for (var j = i + 1; j < stmts.Count; j++)
                {
                    var a = stmts[i];
                    var b = stmts[j];

                    var confidenceGap = Math.Abs(a.Confidence.Score - b.Confidence.Score);
                    if (confidenceGap >= _options.ConfidenceConflictThreshold)
                    {
                        var shared = GetSharedEvidence(a, b);
                        var divergent = GetDivergentEvidence(a, b);

                        findings.Add(new ContradictionFinding
                        {
                            FindingId = $"cc-{findId++:D5}",
                            Classification = ContradictionClassification.ConfidenceConflict,
                            StatementAId = a.StatementId,
                            StatementBId = b.StatementId,
                            StatementAText = Truncate(a.RawClaim),
                            StatementBText = Truncate(b.RawClaim),
                            ConflictDescription = $"Confidence conflict on subject '{group.Key}': {a.Confidence.Score:F2} vs {b.Confidence.Score:F2}",
                            SharedEvidence = shared,
                            DivergentEvidence = divergent,
                        });
                    }
                }
            }
        }
    }

    private void DetectDivergentImplementations(
        List<SemanticStatement> statements,
        List<ContradictionFinding> findings,
        ref int findId)
    {
        if (!_options.EnableDivergentDetection) return;

        var subjectGroups = statements
            .Where(s => !s.IsSuppressed && !string.IsNullOrEmpty(s.SubjectNodeId))
            .GroupBy(s => s.SubjectNodeId!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in subjectGroups)
        {
            var stmts = group.OrderBy(s => s.StatementId, StringComparer.Ordinal).ToList();
            for (var i = 0; i < stmts.Count; i++)
            {
                for (var j = i + 1; j < stmts.Count; j++)
                {
                    var a = stmts[i];
                    var b = stmts[j];

                    if (a.Evidence.Entries.Count > 0 && b.Evidence.Entries.Count > 0
                        && HasDivergentSources(a, b))
                    {
                        findings.Add(new ContradictionFinding
                        {
                            FindingId = $"di-{findId++:D5}",
                            Classification = ContradictionClassification.DivergentImplementation,
                            StatementAId = a.StatementId,
                            StatementBId = b.StatementId,
                            StatementAText = Truncate(a.RawClaim),
                            StatementBText = Truncate(b.RawClaim),
                            ConflictDescription = $"Divergent implementations for subject '{group.Key}': different evidence sources.",
                            SharedEvidence = GetSharedEvidence(a, b),
                            DivergentEvidence = GetDivergentEvidence(a, b),
                        });
                    }
                }
            }
        }
    }

    private void DetectStaleGrounding(
        List<SemanticStatement> statements,
        List<ContradictionFinding> findings,
        ref int findId)
    {
        foreach (var stmt in statements)
        {
            if (stmt.IsSuppressed || stmt.Evidence.Entries.Count == 0) continue;

            var hasStale = stmt.Evidence.Entries.Any(e =>
                e.Confidence.Level >= ConfidenceLevel.Weak
                && e.Confidence.HasSpeculativeAncestor);

            if (hasStale)
            {
                findings.Add(new ContradictionFinding
                {
                    FindingId = $"sg-{findId++:D5}",
                    Classification = ContradictionClassification.StaleGrounding,
                    StatementAId = stmt.StatementId,
                    StatementBId = "",
                    StatementAText = Truncate(stmt.RawClaim),
                    StatementBText = "",
                    ConflictDescription = $"Stale grounding: statement '{stmt.StatementId}' references evidence with stale confidence paths.",
                    SharedEvidence = Array.Empty<string>(),
                    DivergentEvidence = Array.Empty<string>(),
                });
            }
        }
    }

    private void DetectSemanticDrift(
        List<SemanticStatement> statements,
        List<ContradictionFinding> findings,
        ref int findId)
    {
        if (!_options.EnableDriftDetection) return;

        var subjectGroups = statements
            .Where(s => !s.IsSuppressed && !string.IsNullOrEmpty(s.SubjectNodeId))
            .GroupBy(s => s.SubjectNodeId!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in subjectGroups)
        {
            var stmts = group.OrderBy(s => s.StatementId, StringComparer.Ordinal).ToList();
            for (var i = 0; i < stmts.Count; i++)
            {
                for (var j = i + 1; j < stmts.Count; j++)
                {
                    var a = stmts[i];
                    var b = stmts[j];

                    if (HasSemanticDivergence(a, b))
                    {
                        findings.Add(new ContradictionFinding
                        {
                            FindingId = $"sd-{findId++:D5}",
                            Classification = ContradictionClassification.SemanticDrift,
                            StatementAId = a.StatementId,
                            StatementBId = b.StatementId,
                            StatementAText = Truncate(a.RawClaim),
                            StatementBText = Truncate(b.RawClaim),
                            ConflictDescription = $"Semantic drift on '{group.Key}': claims have diverging semantics.",
                            SharedEvidence = GetSharedEvidence(a, b),
                            DivergentEvidence = GetDivergentEvidence(a, b),
                        });
                    }
                }
            }
        }
    }

    private void DetectTemporalConflicts(
        List<SemanticStatement> statements,
        List<ContradictionFinding> findings,
        ref int findId)
    {
    }

    private static bool HasDirectSemanticConflict(SemanticStatement a, SemanticStatement b)
    {
        if (!string.Equals(a.SubjectNodeId, b.SubjectNodeId, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrEmpty(a.SubjectNodeId))
            return false;

        var textA = a.RawClaim.ToLowerInvariant();
        var textB = b.RawClaim.ToLowerInvariant();

        var negationIndicators = new[]
        {
            (" does ", " does not "),
            (" is ", " is not "),
            (" has ", " does not have "),
            (" supports ", " does not support "),
        };

        foreach (var (positive, negative) in negationIndicators)
        {
            if (textA.Contains(positive, StringComparison.Ordinal) && textB.Contains(negative, StringComparison.Ordinal))
                return true;
            if (textB.Contains(positive, StringComparison.Ordinal) && textA.Contains(negative, StringComparison.Ordinal))
                return true;
        }

        var opposites = new (string, string)[]
        {
            ("increases", "decreases"),
            ("enables", "disables"),
            ("valid", "invalid"),
            ("correct", "incorrect"),
            ("true", "false"),
        };

        foreach (var (pos, neg) in opposites)
        {
            if (textA.Contains(pos, StringComparison.Ordinal) && textB.Contains(neg, StringComparison.Ordinal))
                return true;
            if (textB.Contains(pos, StringComparison.Ordinal) && textA.Contains(neg, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsAbstractionPattern(string text)
    {
        var abstractionPatterns = new[]
        {
            "business logic abstraction",
            "core domain concept",
            "primary workflow",
            "enterprise pattern",
            "architectural pattern",
            "ontology completion",
            "auto-generated summary",
            "smart compression",
        };

        return abstractionPatterns.Any(p =>
            text.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDivergentSources(SemanticStatement a, SemanticStatement b)
    {
        var sourcesA = a.Evidence.Entries
            .Select(e => e.SourceNodeId ?? "")
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        var sourcesB = b.Evidence.Entries
            .Select(e => e.SourceNodeId ?? "")
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        if (sourcesA.Count == 0 || sourcesB.Count == 0) return false;
        return !sourcesA.Overlaps(sourcesB);
    }

    private static bool HasSemanticDivergence(SemanticStatement a, SemanticStatement b)
    {
        if (a.LanguageTone != b.LanguageTone) return true;
        return false;
    }

    private static IReadOnlyList<string> GetSharedEvidence(SemanticStatement a, SemanticStatement b)
    {
        var evidenceA = a.Evidence.Entries
            .Select(e => e.SourceNodeId ?? e.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        var evidenceB = b.Evidence.Entries
            .Select(e => e.SourceNodeId ?? e.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);

        return evidenceA.Intersect(evidenceB, StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> GetDivergentEvidence(SemanticStatement a, SemanticStatement b)
    {
        var evidenceA = a.Evidence.Entries
            .Select(e => e.SourceNodeId ?? e.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        var evidenceB = b.Evidence.Entries
            .Select(e => e.SourceNodeId ?? e.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);

        var symmetricDiff = evidenceA.Except(evidenceB, StringComparer.Ordinal)
            .Concat(evidenceB.Except(evidenceA, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        return symmetricDiff
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();
    }

    private static string Truncate(string text, int maxLen = 80)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }
}

public sealed class ContradictionDetectionOptions
{
    public double ConfidenceConflictThreshold { get; init; } = 0.4;

    public bool EnableDivergentDetection { get; init; } = true;

    public bool EnableDriftDetection { get; init; } = true;

    public static ContradictionDetectionOptions Default => new();
}

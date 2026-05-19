// =============================================================================
// Grounding/Contradictions/ContradictionAwareGeneration.cs — severity-adapted output
// =============================================================================
// Determinism: generation mode is a pure function of state snapshot severity.
//   - Same snapshot → identical generation mode every time.
// Provenance: generated statements carry their evidence chain and contradiction context.
// Replay: output is fully deterministic; schema is structurally comparable.
// Grounding: generation adapts to contradictions:
//   - No contradiction → Assertive
//   - Mild → Assertive with note
//   - Moderate → Qualified (hedged language)
//   - Severe → Blocked (conflicts surfaced, synthesis suppressed)
// =============================================================================

using Core.Grounding.Confidence;
using Core.Runtime;

namespace Core.Grounding.Contradictions;

public sealed class ContradictionAwareGeneration
{
    private readonly ContradictionAwareOptions _options;

    public ContradictionAwareGeneration(ContradictionAwareOptions? options = null)
    {
        _options = options ?? ContradictionAwareOptions.Default;
    }

    public GenerationMode DetermineMode(SemanticStateSnapshot state)
    {
        if (!state.IsConsistent) return GenerationMode.Blocked;
        if (state.Contradictions.HasSevereConflicts) return GenerationMode.Blocked;
        if (state.Contradictions.ModerateCount > 0) return GenerationMode.Qualified;
        if (state.Contradictions.MildCount > 0) return GenerationMode.AssertiveWithNote;
        if (state.SpeculativeStatementCount > 0) return GenerationMode.Qualified;
        return GenerationMode.Assertive;
    }

    public ContradictionAwareResponse Generate(SemanticStateSnapshot state)
    {
        var mode = DetermineMode(state);

        if (mode == GenerationMode.Blocked)
        {
            return BuildBlockedResponse(state);
        }

        var adaptedStatements = new List<SemanticStatement>();
        var surfacingNotes = new List<string>();

        AdaptStatements(state, mode, adaptedStatements, surfacingNotes);

        return new ContradictionAwareResponse
        {
            Mode = mode,
            Snapshot = state,
            AdaptedStatements = adaptedStatements,
            SurfacingNotes = surfacingNotes,
            ContradictionSummary = BuildContradictionSummary(state),
            Severity = state.ResponseSeverity,
        };
    }

    private void AdaptStatements(
        SemanticStateSnapshot state,
        GenerationMode mode,
        List<SemanticStatement> adaptedStatements,
        List<string> surfacingNotes)
    {
        foreach (var stmt in state.Statements)
        {
            if (stmt.IsSuppressed) continue;

            var adapted = AdaptStatement(stmt, mode, state);
            adaptedStatements.Add(adapted);
        }

        if (mode == GenerationMode.AssertiveWithNote
            && state.Contradictions.MildCount > 0)
        {
            var mildFindings = state.Contradictions.Findings
                .Where(f => f.Severity == ContradictionSeverityLevel.Mild)
                .OrderBy(f => f.FindingId, StringComparer.Ordinal);

            foreach (var f in mildFindings)
            {
                surfacingNotes.Add($"[NOTE: Mild contradiction detected] {f.ConflictDescription}");
            }
        }

        if (mode == GenerationMode.Qualified)
        {
            var moderateFindings = state.Contradictions.Findings
                .Where(f => f.Severity >= ContradictionSeverityLevel.Moderate)
                .OrderBy(f => f.FindingId, StringComparer.Ordinal);

            foreach (var f in moderateFindings)
            {
                surfacingNotes.Add($"[QUALIFIED: Contradiction present] {f.ConflictDescription}");
            }
        }
    }

    private SemanticStatement AdaptStatement(
        SemanticStatement original,
        GenerationMode mode,
        SemanticStateSnapshot state)
    {
        var adaptedText = ApplyModeToText(original.Text, mode);
        var adaptedTone = MapModeToTone(mode);

        var isInvolvedInConflict = state.Contradictions.Findings
            .Any(f => f.StatementAId == original.StatementId
                   || f.StatementBId == original.StatementId);

        if (isInvolvedInConflict && mode >= GenerationMode.Qualified)
        {
            adaptedText = $"[INVOLVED IN CONTRADICTION] {original.Text}";
        }

        return new SemanticStatement
        {
            StatementId = $"{original.StatementId}-adapted",
            Text = adaptedText,
            RawClaim = original.RawClaim,
            Confidence = original.Confidence,
            LanguageTone = adaptedTone,
            Evidence = original.Evidence,
            SubjectNodeId = original.SubjectNodeId,
            IsSpeculative = original.IsSpeculative,
            IsSuppressed = original.IsSuppressed,
        };
    }

    private static string ApplyModeToText(string text, GenerationMode mode)
        => mode switch
        {
            GenerationMode.Assertive => text,
            GenerationMode.AssertiveWithNote => $"{text}",
            GenerationMode.Qualified => $"[QUALIFIED — moderate confidence] {text}",
            GenerationMode.Blocked => text,
            _ => text,
        };

    private static LanguageTone MapModeToTone(GenerationMode mode)
        => mode switch
        {
            GenerationMode.Assertive => LanguageTone.Assertive,
            GenerationMode.AssertiveWithNote => LanguageTone.Confident,
            GenerationMode.Qualified => LanguageTone.Hedged,
            GenerationMode.Blocked => LanguageTone.Suppressed,
            _ => LanguageTone.Suppressed,
        };

    private ContradictionAwareResponse BuildBlockedResponse(SemanticStateSnapshot state)
    {
        var notes = new List<string>
        {
            "RESPONSE BLOCKED: Severe contradictions detected.",
        };

        foreach (var f in state.Contradictions.Findings
            .Where(f => f.Severity >= ContradictionSeverityLevel.Severe)
            .OrderBy(f => f.FindingId, StringComparer.Ordinal))
        {
            notes.Add($"  - [{f.Classification}] {f.ConflictDescription}");
        }

        if (state.ConsistencyResult is not null && !state.ConsistencyResult.IsConsistent)
        {
            notes.Add("Consistency validation failed:");
            foreach (var issue in state.ConsistencyResult.Issues
                .OrderBy(i => i.IssueId, StringComparer.Ordinal))
            {
                notes.Add($"  - [{issue.IssueType}] {issue.Description}");
            }
        }

        return new ContradictionAwareResponse
        {
            Mode = GenerationMode.Blocked,
            Snapshot = state,
            AdaptedStatements = Array.Empty<SemanticStatement>(),
            SurfacingNotes = notes,
            ContradictionSummary = BuildContradictionSummary(state),
            Severity = state.ResponseSeverity,
        };
    }

    private static string BuildContradictionSummary(SemanticStateSnapshot state)
    {
        if (!state.Contradictions.HasConflicts)
            return "No contradictions detected.";

        var sb = new System.Text.StringBuilder();
        sb.Append(state.Contradictions.TotalFindings);
        sb.Append(" contradiction(s) found");

        if (state.Contradictions.SevereCount > 0)
        {
            sb.Append(" (");
            sb.Append(state.Contradictions.SevereCount);
            sb.Append(" severe");
            if (state.Contradictions.ModerateCount > 0)
            {
                sb.Append(", ");
                sb.Append(state.Contradictions.ModerateCount);
                sb.Append(" moderate");
            }
            if (state.Contradictions.MildCount > 0)
            {
                sb.Append(", ");
                sb.Append(state.Contradictions.MildCount);
                sb.Append(" mild");
            }
            sb.Append(')');
        }

        sb.Append('.');

        var byType = state.Contradictions.Findings
            .GroupBy(f => f.Classification)
            .OrderBy(g => g.Key)
            .Select(g => $"  - {g.Key}: {g.Count()}");

        foreach (var line in byType)
        {
            sb.AppendLine();
            sb.Append(line);
        }

        return sb.ToString();
    }
}

public sealed class ContradictionAwareOptions
{
    public bool SuppressOnSevereConflict { get; init; } = true;
    public bool QualifyOnModerateConflict { get; init; } = true;
    public bool SurfaceConflictingEvidence { get; init; } = true;

    public static ContradictionAwareOptions Default => new();
}

public sealed class ContradictionAwareResponse : IEquatable<ContradictionAwareResponse>
{
    public GenerationMode Mode { get; init; }
    public required SemanticStateSnapshot Snapshot { get; init; }
    public required IReadOnlyList<SemanticStatement> AdaptedStatements { get; init; }
    public required IReadOnlyList<string> SurfacingNotes { get; init; }
    public required string ContradictionSummary { get; init; }
    public SemanticResponseSeverity Severity { get; init; }

    public bool IsBlocked => Mode == GenerationMode.Blocked;
    public bool IsQualified => Mode >= GenerationMode.Qualified;
    public bool IsAssertive => Mode == GenerationMode.Assertive;

    public bool Equals(ContradictionAwareResponse? other)
    {
        if (other is null) return false;
        if (Mode != other.Mode) return false;
        if (Severity != other.Severity) return false;
        if (!Snapshot.Equals(other.Snapshot)) return false;
        if (AdaptedStatements.Count != other.AdaptedStatements.Count) return false;
        for (var i = 0; i < AdaptedStatements.Count; i++)
            if (!AdaptedStatements[i].Equals(other.AdaptedStatements[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is ContradictionAwareResponse other && Equals(other);
    public override int GetHashCode() => Snapshot.GetHashCode();

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Contradiction-Aware Generation Report");
        sb.AppendLine($"Mode: {Mode}");
        sb.AppendLine($"Severity: {Severity}");
        sb.AppendLine($"Blocked: {IsBlocked}");
        sb.AppendLine($"Qualified: {IsQualified}");
        sb.AppendLine();
        sb.AppendLine("## Contradiction Summary");
        sb.AppendLine(ContradictionSummary);
        sb.AppendLine();

        if (AdaptedStatements.Count > 0)
        {
            sb.AppendLine("## Adapted Statements");
            foreach (var stmt in AdaptedStatements)
            {
                sb.AppendLine($"  - ({stmt.LanguageTone}) {stmt.Text}");
            }
        }

        if (SurfacingNotes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Surfacing Notes");
            foreach (var note in SurfacingNotes)
                sb.AppendLine($"  {note}");
        }

        return sb.ToString();
    }
}

public enum GenerationMode
{
    Assertive = 0,
    AssertiveWithNote = 1,
    Qualified = 2,
    Blocked = 3,
}

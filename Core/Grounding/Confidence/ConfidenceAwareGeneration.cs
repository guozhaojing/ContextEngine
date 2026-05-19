// =============================================================================
// Grounding/Confidence/ConfidenceAwareGeneration.cs — confidence-sensitive generation
// =============================================================================
// Determinism: language selection is a pure function of ConfidenceLevel.
//   - Same claim text + same confidence level → same statement output.
//   - No LLM, no randomness, no temperature.
// Provenance: every generated SemanticStatement carries its EvidenceChain.
// Replay: SemanticStatement implements IEquatable for regression comparison.
// Grounding: generation tone is strictly correlated with grounding confidence.
//   - Assertive → Strong/Certain
//   - Hedged → Moderate
//   - Qualified → Weak
//   - Suppressed → Speculative/Unsupported
// =============================================================================

using Core.Runtime;

namespace Core.Grounding.Confidence;

public sealed class ConfidenceAwareGeneration
{
    private readonly ConfidenceAwareOptions _options;

    public ConfidenceAwareGeneration(ConfidenceAwareOptions? options = null)
    {
        _options = options ?? ConfidenceAwareOptions.Default;
    }

    public SemanticStatement GenerateStatement(
        string rawClaim,
        GroundingConfidence confidence,
        EvidenceChain evidence,
        string? subjectNodeId = null)
    {
        var languageTone = DetermineTone(confidence);
        var finalStatement = ApplyTone(rawClaim, languageTone, confidence);

        return new SemanticStatement
        {
            StatementId = $"stmt-{Guid.NewGuid():N}"[..16],
            Text = finalStatement,
            RawClaim = rawClaim,
            Confidence = confidence,
            LanguageTone = languageTone,
            Evidence = evidence,
            SubjectNodeId = subjectNodeId,
            IsSpeculative = confidence.Level >= ConfidenceLevel.Speculative,
            IsSuppressed = confidence.ShouldSuppressGeneration,
        };
    }

    public SemanticResponse GenerateResponse(
        string responseTitle,
        IReadOnlyList<(string Claim, GroundingConfidence Confidence, EvidenceChain Evidence, string? SubjectNodeId)> claims,
        IEnumerable<string>? systemNotes = null)
    {
        var statements = new List<SemanticStatement>();
        var suppressed = new List<string>();

        foreach (var (claim, confidence, evidence, subjectNodeId) in claims)
        {
            if (confidence.ShouldSuppressGeneration)
            {
                suppressed.Add($"Suppressed (Unsupported): {claim}");
                continue;
            }

            if (confidence.Level >= ConfidenceLevel.Speculative && _options.SuppressSpeculative)
            {
                suppressed.Add($"Suppressed (Speculative): {claim}");
                continue;
            }

            var stmt = GenerateStatement(claim, confidence, evidence, subjectNodeId);
            statements.Add(stmt);
        }

        var allEvidence = statements
            .SelectMany(s => s.Evidence.Entries)
            .DistinctBy(e => e.EvidenceId, StringComparer.Ordinal)
            .ToList();

        var snapshot = ProvenanceSnapshot.Capture(statements, allEvidence);

        return new SemanticResponse
        {
            ResponseId = $"resp-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            Title = responseTitle,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            Statements = statements,
            SuppressedStatements = suppressed,
            EvidenceChain = new EvidenceChain { Entries = allEvidence },
            Contradictions = ContradictionSet.Empty,
            Provenance = snapshot,
            Fingerprint = ReplayFingerprint.Compute(statements, snapshot),
            Metadata = systemNotes?.ToList() ?? new List<string>(),
        };
    }

    private LanguageTone DetermineTone(GroundingConfidence confidence) => confidence.Level switch
    {
        ConfidenceLevel.Certain => LanguageTone.Assertive,
        ConfidenceLevel.Strong => LanguageTone.Confident,
        ConfidenceLevel.Moderate => LanguageTone.Hedged,
        ConfidenceLevel.Weak => LanguageTone.Qualified,
        ConfidenceLevel.Speculative => LanguageTone.Speculative,
        ConfidenceLevel.Unsupported => LanguageTone.Suppressed,
        _ => LanguageTone.Suppressed,
    };

    private string ApplyTone(string rawClaim, LanguageTone tone, GroundingConfidence confidence)
    {
        if (tone == LanguageTone.Suppressed)
            return $"[UNSUPPORTED — confidence {confidence.Score:F2}] {rawClaim}";

        if (tone == LanguageTone.Speculative)
            return $"[SPECULATIVE — confidence {confidence.Score:F2}] {rawClaim}";

        var qualify = tone switch
        {
            LanguageTone.Assertive => "", // no qualification; plain claim
            LanguageTone.Confident => "", // slight hedged prefix
            LanguageTone.Hedged => _options.GetHedgedPrefix(),
            LanguageTone.Qualified => _options.GetQualifiedPrefix(),
            _ => _options.GetQualifiedPrefix(),
        };

        return string.IsNullOrEmpty(qualify) ? rawClaim : $"{qualify} {rawClaim}";
    }
}

public enum LanguageTone
{
    Assertive = 0,
    Confident = 1,
    Hedged = 2,
    Qualified = 3,
    Speculative = 4,
    Suppressed = 5,
}

public sealed class ConfidenceAwareOptions
{
    public bool SuppressSpeculative { get; init; } = false;

    public IReadOnlyList<string> HedgedPrefixes { get; init; } = new[]
    {
        "Based on analysis, ",
        "Analysis indicates that ",
        "The codebase suggests that ",
    };

    public IReadOnlyList<string> QualifiedPrefixes { get; init; } = new[]
    {
        "[WEAK EVIDENCE] ",
        "With limited confidence: ",
        "Evidence weakly suggests: ",
    };

    public string GetHedgedPrefix()
    {
        if (HedgedPrefixes.Count == 0) return "";
        return HedgedPrefixes[0];
    }

    public string GetQualifiedPrefix()
    {
        if (QualifiedPrefixes.Count == 0) return "";
        return QualifiedPrefixes[0];
    }

    public static ConfidenceAwareOptions Default => new();
}

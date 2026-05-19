// =============================================================================
// Grounding/HallucinationBlocker.cs — prevents unsupported abstractions
// =============================================================================
// Determinism: all pattern matching is static string comparison, no ML/LLM.
//   - Block decisions are derived exclusively from claim text, classification,
//     and evidence counts.
//   - Same inputs → identical block decision.
// Provenance: block decisions carry the specific evidence deficiency.
// Replay: BlockDecision is immutable and structurally comparable.
// Grounding: blocks: ungrounded claims, speculative abstractions, pattern-matched
//   hallucinations, zero-evidence claims.
// =============================================================================

using Core.Truth;

namespace Core.Grounding;

public sealed class HallucinationBlocker
{
    private readonly HallucinationBlockerOptions _options;

    public HallucinationBlocker(HallucinationBlockerOptions? options = null)
    {
        _options = options ?? HallucinationBlockerOptions.Default;
    }

    public BlockDecision Evaluate(ClaimValidationResult validationResult)
    {
        var reasons = new List<string>();

        if (!_options.Enabled) return BlockDecision.Allow(validationResult.Claim);

        var classification = validationResult.Classification;
        var claim = validationResult.Claim;

        CheckClassification(classification, reasons);
        CheckEvidenceDeficiency(validationResult, reasons);
        CheckForbiddenPatterns(claim.ClaimText, reasons);
        CheckZeroEvidence(validationResult, reasons);
        CheckUngroundedAbstraction(validationResult, reasons);
        CheckUnsupportedSpeculation(classification, reasons);

        var blocked = reasons.Count > 0;

        return new BlockDecision
        {
            Claim = claim,
            Blocked = blocked,
            Classification = classification,
            Reasons = reasons,
        };
    }

    private void CheckClassification(ClaimClassification classification, List<string> reasons)
    {
        if (classification == ClaimClassification.Hallucinated)
        {
            reasons.Add($"Claim classified as {nameof(ClaimClassification.Hallucinated)}.");
        }
        else if (classification == ClaimClassification.Speculative && _options.BlockSpeculative)
        {
            reasons.Add($"Speculative claims are blocked by configuration.");
        }
    }

    private void CheckEvidenceDeficiency(ClaimValidationResult result, List<string> reasons)
    {
        if (result.SupportingEdgeCount < _options.MinSupportingEdges)
        {
            reasons.Add($"Insufficient supporting edges: {result.SupportingEdgeCount} < {_options.MinSupportingEdges}.");
        }

        if (result.SupportingFilePathCount < _options.MinSupportingFiles)
        {
            reasons.Add($"Insufficient supporting files: {result.SupportingFilePathCount} < {_options.MinSupportingFiles}.");
        }

        if (_options.RequireSymbolBinding && !result.HasSymbolBinding)
        {
            reasons.Add("Symbol binding required but absent.");
        }

        if (_options.RequireGraphEvidence && !result.HasGraphEvidence)
        {
            reasons.Add("Graph evidence required but absent.");
        }

        if (_options.RequireTraversalEvidence && !result.HasTraversalEvidence)
        {
            reasons.Add("Traversal evidence required but absent.");
        }

        if (_options.RequireProvenanceChain && !result.HasProvenanceChain)
        {
            reasons.Add("Provenance chain required but absent.");
        }
    }

    private void CheckForbiddenPatterns(string claimText, List<string> reasons)
    {
        foreach (var pattern in _options.ForbiddenPatterns)
        {
            if (claimText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"Forbidden hallucination pattern detected: '{pattern}'.");
            }
        }
    }

    private void CheckZeroEvidence(ClaimValidationResult result, List<string> reasons)
    {
        if (result.SupportingEdgeCount == 0 && result.SupportingFilePathCount == 0 && !result.HasSymbolBinding)
        {
            reasons.Add("Claim has zero supporting evidence.");
        }
    }

    private void CheckUngroundedAbstraction(ClaimValidationResult result, List<string> reasons)
    {
        var text = result.Claim.ClaimText;
        foreach (var abstraction in _options.ForbiddenAbstractions)
        {
            if (text.Contains(abstraction, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"Ungrounded abstraction detected: '{abstraction}'.");
            }
        }
    }

    private void CheckUnsupportedSpeculation(ClaimClassification classification, List<string> reasons)
    {
        if (!_options.BlockInferred && !_options.BlockSpeculative) return;
        if (!_options.BlockInferred && classification == ClaimClassification.Inferred) return;
        if (!_options.BlockSpeculative && classification == ClaimClassification.Speculative) return;
    }
}

public sealed class HallucinationBlockerOptions
{
    public bool Enabled { get; init; } = true;

    public bool BlockSpeculative { get; init; } = true;

    public bool BlockInferred { get; init; } = false;

    public int MinSupportingEdges { get; init; } = 0;

    public int MinSupportingFiles { get; init; } = 0;

    public bool RequireSymbolBinding { get; init; } = true;

    public bool RequireGraphEvidence { get; init; } = true;

    public bool RequireTraversalEvidence { get; init; } = false;

    public bool RequireProvenanceChain { get; init; } = false;

    public IReadOnlyList<string> ForbiddenPatterns { get; init; } = new[]
    {
        "business logic abstraction",
        "core domain concept",
        "primary workflow",
        "enterprise pattern",
        "architectural pattern",
        "auto-generated summary",
        "smart compression",
        "ontology completion",
        "This section was auto-completed",
        "Generated by ontology expansion",
        "Inferred entity relationship",
        "Derived business abstraction",
        "propagated entity",
        "inferred from",
        "ungrounded edge",
        "inferred path",
        "speculative relationship",
        "likely implementation",
        "probable pattern",
    };

    public IReadOnlyList<string> ForbiddenAbstractions { get; init; } = new[]
    {
        "business logic abstraction",
        "core domain concept",
        "primary workflow",
        "enterprise pattern",
        "architectural pattern",
    };

    public static HallucinationBlockerOptions Default => new();
}

public sealed class BlockDecision : IEquatable<BlockDecision>
{
    public required ClaimSubject Claim { get; init; }
    public bool Blocked { get; init; }
    public ClaimClassification Classification { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }

    public bool IsAllowed => !Blocked;

    public static BlockDecision Allow(ClaimSubject claim) => new()
    {
        Claim = claim,
        Blocked = false,
        Classification = ClaimClassification.Grounded,
        Reasons = Array.Empty<string>(),
    };

    public bool Equals(BlockDecision? other)
    {
        if (other is null) return false;
        if (Blocked != other.Blocked) return false;
        if (Classification != other.Classification) return false;
        if (!Claim.Equals(other.Claim)) return false;
        if (Reasons.Count != other.Reasons.Count) return false;
        for (var i = 0; i < Reasons.Count; i++)
            if (!StringComparer.Ordinal.Equals(Reasons[i], other.Reasons[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is BlockDecision other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Claim.ClaimId, Blocked, Classification);

    public override string ToString() =>
        Blocked
            ? $"BLOCKED [{Classification}] {Claim.ClaimText} — {string.Join("; ", Reasons)}"
            : $"ALLOWED [{Classification}] {Claim.ClaimText}";
}

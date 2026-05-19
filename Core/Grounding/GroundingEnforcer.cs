// =============================================================================
// Grounding/GroundingEnforcer.cs — enforcement orchestration + audit output
// =============================================================================
// Determinism: the enforcer is a pure orchestrator; all decisions delegated to
//   deterministic validators and blockers. Execution order is fixed.
// Provenance: the GroundingAudit captures the full pipeline trace including all
//   evidence, classifications, block decisions, and citations.
// Replay: GroundingAudit is immutable and structurally comparable (Equals).
// Grounding: every claim flows through Validate → Block → Cite → Audit.
// Tie-breaking: audit sections are ordered deterministically by claim ID.
// =============================================================================

using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Semantics;

namespace Core.Grounding;

public sealed class GroundingEnforcer
{
    private readonly GroundedClaimValidator _validator;
    private readonly HallucinationBlocker _blocker;
    private readonly CitationConstrainedGeneration _citation;
    private readonly GraphIndex _graphIndex;
    private readonly SymbolReferenceIndex _symbolIndex;

    public GroundingEnforcer(
        GraphIndex graphIndex,
        SymbolReferenceIndex symbolIndex,
        GroundingEnforcerOptions? options = null)
    {
        _graphIndex = graphIndex ?? throw new ArgumentNullException(nameof(graphIndex));
        _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));

        var opts = options ?? GroundingEnforcerOptions.Default;
        _validator = new GroundedClaimValidator(graphIndex, symbolIndex, opts.ValidatorOptions);
        _blocker = new HallucinationBlocker(opts.BlockerOptions);
        _citation = new CitationConstrainedGeneration(opts.CitationOptions);
    }

    public GroundingEnforcementResult Enforce(
        IReadOnlyList<ClaimSubject> claims,
        IReadOnlyList<GroundingEvidence> provenanceChain,
        IReadOnlyList<SemanticPath> traversalEvidence)
    {
        var validations = new List<ClaimValidationResult>();
        var blocked = new List<BlockDecision>();
        var passed = new List<ClaimValidationResult>();

        foreach (var claim in claims.OrderBy(c => c.ClaimId, StringComparer.Ordinal))
        {
            var validation = _validator.Validate(claim, provenanceChain, traversalEvidence);
            validations.Add(validation);

            var decision = _blocker.Evaluate(validation);
            blocked.Add(decision);

            if (!decision.Blocked)
                passed.Add(validation);
        }

        var evIdCounter = 0;
        var evidenceForCitations = validations
            .Where(v => v.HasGraphEvidence)
            .Select(v => BuildEvidence(v, ref evIdCounter))
            .ToList();

        var builder = _citation.CreateBuilder();
        foreach (var validation in passed)
        {
            var relevantEvidence = evidenceForCitations
                .Where(e => e.GraphNodeIds.Any(nid =>
                    validation.EvidenceNodeIds.Contains(nid, StringComparer.Ordinal)))
                .ToList();

            if (relevantEvidence.Count == 0)
                relevantEvidence = evidenceForCitations.Take(1).ToList();

            builder.AddStatement(validation.Claim.ClaimText, relevantEvidence);
        }

        var citationResult = _citation.Finalize(builder);

        var blockResult = new BlockResult
        {
            TotalEvaluated = blocked.Count,
            TotalBlocked = blocked.Count(b => b.Blocked),
            TotalAllowed = blocked.Count(b => !b.Blocked),
            Decisions = blocked,
        };

        return new GroundingEnforcementResult
        {
            Validations = validations,
            BlockResult = blockResult,
            CitationResult = citationResult,
            ProvenanceChain = provenanceChain,
            TraversalEvidence = traversalEvidence,
        };
    }

    private GroundingEvidence BuildEvidence(ClaimValidationResult validation, ref int evIdCounter)
    {
        var evId = $"ev-{evIdCounter++:D6}";
        return new GroundingEvidence
        {
            EvidenceId = evId,
            SourceChunks = Array.Empty<Retrieval.Chunking.CodeChunk>(),
            SourceSymbols = validation.EvidenceSymbolHandles
                .Select(SymbolHandle.Parse)
                .Where(h => !h.IsEmpty)
                .ToList(),
            GraphNodeIds = validation.EvidenceNodeIds,
            EdgeDescs = validation.EvidenceEdgeKinds
                .Select(k => new EdgeEvidenceDesc("", "", k, Core.Truth.TruthScore.Medium(
                    Core.Truth.EvidenceStrength.SyntaxPattern, Core.Truth.TruthSource.AnalyzerInferred)))
                .ToList(),
            SupportingFiles = validation.EvidenceSourceFiles,
        };
    }

    public GroundingAudit BuildAudit(GroundingEnforcementResult result)
    {
        var entries = new List<AuditEntry>();

        foreach (var validation in result.Validations.OrderBy(v => v.Claim.ClaimId, StringComparer.Ordinal))
        {
            var blockDecision = result.BlockResult.Decisions
                .FirstOrDefault(d => d.Claim.ClaimId == validation.Claim.ClaimId);

            entries.Add(new AuditEntry
            {
                ClaimId = validation.Claim.ClaimId,
                ClaimText = validation.Claim.ClaimText,
                Classification = validation.Classification,
                Blocked = blockDecision?.Blocked ?? false,
                BlockReasons = blockDecision?.Reasons ?? Array.Empty<string>(),
                Confidence = validation.Confidence,
                EdgeCount = validation.SupportingEdgeCount,
                FileCount = validation.SupportingFilePathCount,
                HasSymbol = validation.HasSymbolBinding,
                HasGraph = validation.HasGraphEvidence,
                HasTraversal = validation.HasTraversalEvidence,
                HasProvenance = validation.HasProvenanceChain,
                FailureReasons = validation.FailureReasons,
            });
        }

        return new GroundingAudit
        {
            AuditId = $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            TotalClaims = result.Validations.Count,
            GroundedCount = result.Validations.Count(v => v.Classification == ClaimClassification.Grounded),
            InferredCount = result.Validations.Count(v => v.Classification == ClaimClassification.Inferred),
            SpeculativeCount = result.Validations.Count(v => v.Classification == ClaimClassification.Speculative),
            HallucinatedCount = result.Validations.Count(v => v.Classification == ClaimClassification.Hallucinated),
            BlockedCount = result.BlockResult.TotalBlocked,
            AcceptedStatementCount = result.CitationResult.TotalAccepted,
            RejectedStatementCount = result.CitationResult.TotalRejected,
            Entries = entries,
        };
    }
}

public sealed class GroundingEnforcerOptions
{
    public GroundedClaimValidatorOptions ValidatorOptions { get; init; } = GroundedClaimValidatorOptions.Default;

    public HallucinationBlockerOptions BlockerOptions { get; init; } = HallucinationBlockerOptions.Default;

    public CitationConstrainedOptions CitationOptions { get; init; } = CitationConstrainedOptions.Default;

    public static GroundingEnforcerOptions Default => new();
}

public sealed class GroundingEnforcementResult
{
    public required IReadOnlyList<ClaimValidationResult> Validations { get; init; }
    public required BlockResult BlockResult { get; init; }
    public required GeneratedStatementSet CitationResult { get; init; }
    public required IReadOnlyList<GroundingEvidence> ProvenanceChain { get; init; }
    public required IReadOnlyList<SemanticPath> TraversalEvidence { get; init; }

    public bool IsFullyGrounded => Validations.Count > 0
        && Validations.All(v => v.Classification == ClaimClassification.Grounded);
}

public sealed class BlockResult
{
    public int TotalEvaluated { get; init; }
    public int TotalBlocked { get; init; }
    public int TotalAllowed { get; init; }
    public required IReadOnlyList<BlockDecision> Decisions { get; init; }
}

public sealed class GroundingAudit : IEquatable<GroundingAudit>
{
    public required string AuditId { get; init; }
    public string GeneratedAt { get; init; } = "";
    public int TotalClaims { get; init; }
    public int GroundedCount { get; init; }
    public int InferredCount { get; init; }
    public int SpeculativeCount { get; init; }
    public int HallucinatedCount { get; init; }
    public int BlockedCount { get; init; }
    public int AcceptedStatementCount { get; init; }
    public int RejectedStatementCount { get; init; }
    public required IReadOnlyList<AuditEntry> Entries { get; init; }

    public bool Equals(GroundingAudit? other)
    {
        if (other is null) return false;
        if (TotalClaims != other.TotalClaims) return false;
        if (GroundedCount != other.GroundedCount) return false;
        if (InferredCount != other.InferredCount) return false;
        if (SpeculativeCount != other.SpeculativeCount) return false;
        if (HallucinatedCount != other.HallucinatedCount) return false;
        if (BlockedCount != other.BlockedCount) return false;
        if (AcceptedStatementCount != other.AcceptedStatementCount) return false;
        if (RejectedStatementCount != other.RejectedStatementCount) return false;
        if (Entries.Count != other.Entries.Count) return false;

        for (var i = 0; i < Entries.Count; i++)
        {
            if (!Entries[i].Equals(other.Entries[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is GroundingAudit other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TotalClaims, GroundedCount, InferredCount, SpeculativeCount, HallucinatedCount, BlockedCount);

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Grounding Enforcement Audit");
        sb.AppendLine($"Audit: {AuditId}");
        sb.AppendLine($"Generated: {GeneratedAt}");
        sb.AppendLine();
        sb.AppendLine($"Summary: {TotalClaims} claims processed");
        sb.AppendLine($"  Grounded:    {GroundedCount}");
        sb.AppendLine($"  Inferred:    {InferredCount}");
        sb.AppendLine($"  Speculative: {SpeculativeCount}");
        sb.AppendLine($"  Hallucinated:{HallucinatedCount}");
        sb.AppendLine($"  Blocked:     {BlockedCount}");
        sb.AppendLine($"  Accepted:    {AcceptedStatementCount}");
        sb.AppendLine($"  Rejected:    {RejectedStatementCount}");
        sb.AppendLine();

        foreach (var entry in Entries)
        {
            var status = entry.Blocked ? "BLOCKED" : "PASSED";
            sb.AppendLine($"## {status} [{entry.Classification}] {entry.ClaimId}");
            sb.AppendLine($"  Claim: {entry.ClaimText}");
            sb.AppendLine($"  Confidence: {entry.Confidence:F2} | Edges: {entry.EdgeCount} | Files: {entry.FileCount}");
            sb.AppendLine($"  Symbol: {entry.HasSymbol} | Graph: {entry.HasGraph} | Traversal: {entry.HasTraversal} | Provenance: {entry.HasProvenance}");

            if (entry.Blocked && entry.BlockReasons.Count > 0)
            {
                sb.AppendLine("  Block reasons:");
                foreach (var reason in entry.BlockReasons)
                    sb.AppendLine($"    - {reason}");
            }

            if (!entry.Blocked && entry.FailureReasons.Count > 0)
            {
                sb.AppendLine("  Warnings:");
                foreach (var reason in entry.FailureReasons)
                    sb.AppendLine($"    - {reason}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public sealed class AuditEntry : IEquatable<AuditEntry>
{
    public required string ClaimId { get; init; }
    public required string ClaimText { get; init; }
    public ClaimClassification Classification { get; init; }
    public bool Blocked { get; init; }
    public required IReadOnlyList<string> BlockReasons { get; init; }
    public double Confidence { get; init; }
    public int EdgeCount { get; init; }
    public int FileCount { get; init; }
    public bool HasSymbol { get; init; }
    public bool HasGraph { get; init; }
    public bool HasTraversal { get; init; }
    public bool HasProvenance { get; init; }
    public required IReadOnlyList<string> FailureReasons { get; init; }

    public bool Equals(AuditEntry? other)
    {
        if (other is null) return false;
        if (Blocked != other.Blocked) return false;
        if (Classification != other.Classification) return false;
        if (!StringComparer.Ordinal.Equals(ClaimId, other.ClaimId)) return false;
        if (Math.Abs(Confidence - other.Confidence) > 0.0001) return false;
        if (EdgeCount != other.EdgeCount) return false;
        if (FileCount != other.FileCount) return false;
        if (HasSymbol != other.HasSymbol) return false;
        if (HasGraph != other.HasGraph) return false;
        if (HasTraversal != other.HasTraversal) return false;
        if (HasProvenance != other.HasProvenance) return false;
        if (BlockReasons.Count != other.BlockReasons.Count) return false;
        for (var i = 0; i < BlockReasons.Count; i++)
            if (!StringComparer.Ordinal.Equals(BlockReasons[i], other.BlockReasons[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is AuditEntry other && Equals(other);
    public override int GetHashCode() => ClaimId.GetHashCode(StringComparison.Ordinal);
}

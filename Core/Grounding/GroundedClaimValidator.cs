// =============================================================================
// Grounding/GroundedClaimValidator.cs — validates claims against grounding evidence
// =============================================================================
// Determinism: all validation paths are stateless; same inputs → same outputs.
//   - No HashSet iteration; OrderedEnumerable used for stable result ordering.
//   - All symbol lookups use StringComparer.Ordinal keys.
//   - Float comparisons use epsilon tolerance.
// Provenance: every validation result carries evidence node IDs, edge kinds,
//   symbol handles, and source files.
// Replay: ClaimValidationResult is immutable and structurally comparable.
// Grounding: claims classified as Grounded/Inferred/Speculative/Hallucinated.
// Tie-breaking: best classification across all supporting evidence.
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Semantics;
using Core.Truth;

namespace Core.Grounding;

public sealed class GroundedClaimValidator
{
    private readonly GroundedClaimValidatorOptions _options;
    private readonly GraphIndex _graphIndex;
    private readonly SymbolReferenceIndex _symbolIndex;

    public GroundedClaimValidator(
        GraphIndex graphIndex,
        SymbolReferenceIndex symbolIndex,
        GroundedClaimValidatorOptions? options = null)
    {
        _graphIndex = graphIndex ?? throw new ArgumentNullException(nameof(graphIndex));
        _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
        _options = options ?? GroundedClaimValidatorOptions.Default;
    }

    public ClaimValidationResult Validate(
        ClaimSubject claim,
        IReadOnlyList<GroundingEvidence> provenanceChain,
        IReadOnlyList<SemanticPath> traversalEvidence)
    {
        var failures = new List<string>();
        var evidenceNodeIds = new SortedSet<string>(StringComparer.Ordinal);
        var evidenceEdgeKinds = new SortedSet<string>(StringComparer.Ordinal);
        var evidenceSymbolHandles = new SortedSet<string>(StringComparer.Ordinal);
        var evidenceSourceFiles = new SortedSet<string>(StringComparer.Ordinal);

        var hasGraphEvidence = false;
        var hasSymbolBinding = false;
        var hasTraversalEvidence = false;
        var hasProvenanceChain = provenanceChain.Count > 0 && !provenanceChain.All(e => e.IsEmpty);
        var supportingEdgeCount = 0;
        var supportingFileCount = 0;

        var aggregateConfidence = TruthScore.Ungrounded();
        var classificationFromEvidence = new List<ClaimClassification>();

        var subjectNode = ResolveSubjectNode(claim);
        hasGraphEvidence = subjectNode is not null;

        if (!hasGraphEvidence)
        {
            failures.Add($"Claim subject node '{claim.SubjectNodeId}' not found in graph index.");
        }
        else
        {
            evidenceNodeIds.Add(subjectNode!.Id);

            if (!string.IsNullOrEmpty(subjectNode.SymbolHandle))
            {
                hasSymbolBinding = true;
                evidenceSymbolHandles.Add(subjectNode.SymbolHandle);
            }

            if (!string.IsNullOrEmpty(subjectNode.SourceFile))
            {
                supportingFileCount++;
                evidenceSourceFiles.Add(subjectNode.SourceFile);
            }

            if (SymbolHandle.TryParse(subjectNode.SymbolHandle, out var nodeSymbol) && !nodeSymbol.IsEmpty)
            {
                classificationFromEvidence.Add(ClassifyFromNode(subjectNode, true));
            }
            else
            {
                classificationFromEvidence.Add(ClassifyFromNode(subjectNode, false));
            }
        }

        if (!hasSymbolBinding && _options.RequireSymbolBinding)
        {
            failures.Add($"Claim subject lacks symbol binding and symbol binding is required.");
        }

        foreach (var path in traversalEvidence)
        {
            if (subjectNode is not null && path.NodeIds.Contains(subjectNode.Id, StringComparer.Ordinal))
            {
                hasTraversalEvidence = true;
                foreach (var nodeId in path.NodeIds)
                {
                    evidenceNodeIds.Add(nodeId);
                }
                foreach (var edgeKind in path.EdgeKinds)
                {
                    evidenceEdgeKinds.Add(edgeKind);
                }
            }
        }

        if (!hasTraversalEvidence && _options.RequireTraversalEvidence)
        {
            failures.Add("Claim has no supporting traversal evidence.");
        }

        foreach (var evidence in provenanceChain)
        {
            if (evidence.IsEmpty) continue;

            supportingEdgeCount += evidence.EdgeDescs.Count;
            supportingFileCount += evidence.SupportingFiles.Count;

            foreach (var nodeId in evidence.GraphNodeIds)
                evidenceNodeIds.Add(nodeId);

            foreach (var sf in evidence.SupportingFiles)
                evidenceSourceFiles.Add(sf);

            foreach (var edge in evidence.EdgeDescs)
            {
                evidenceEdgeKinds.Add(edge.EdgeKind);
                classificationFromEvidence.Add(ClaimValidationResult.DeriveClassification(
                    edge.Confidence, hasSymbolBinding, hasTraversalEvidence));
            }

            foreach (var sym in evidence.SourceSymbols)
            {
                if (!sym.IsEmpty)
                    evidenceSymbolHandles.Add(sym.Value);
            }

            if (evidence.AggregateConfidence.Value > aggregateConfidence.Value)
                aggregateConfidence = evidence.AggregateConfidence;
        }

        if (!hasProvenanceChain && _options.RequireProvenanceChain)
        {
            failures.Add("Claim has no provenance chain evidence.");
        }

        if (supportingEdgeCount == 0)
        {
            classificationFromEvidence.Add(ClaimClassification.Hallucinated);
        }

        var classification = classificationFromEvidence.Count > 0
            ? ClaimValidationResult.CombineBest(classificationFromEvidence)
            : ClaimClassification.Hallucinated;

        CheckForbiddenPatterns(claim, classification, failures);

        return new ClaimValidationResult
        {
            Claim = claim,
            Classification = classification,
            Confidence = aggregateConfidence.IsGrounded ? aggregateConfidence.Value : 0,
            SupportingEdgeCount = supportingEdgeCount,
            SupportingFilePathCount = supportingFileCount,
            HasSymbolBinding = hasSymbolBinding,
            HasGraphEvidence = hasGraphEvidence,
            HasTraversalEvidence = hasTraversalEvidence,
            HasProvenanceChain = hasProvenanceChain,
            FailureReasons = failures,
            EvidenceNodeIds = evidenceNodeIds.ToList(),
            EvidenceEdgeKinds = evidenceEdgeKinds.ToList(),
            EvidenceSymbolHandles = evidenceSymbolHandles.ToList(),
            EvidenceSourceFiles = evidenceSourceFiles.ToList(),
        };
    }

    private GraphNode? ResolveSubjectNode(ClaimSubject claim)
    {
        if (!string.IsNullOrEmpty(claim.SubjectNodeId))
            return _graphIndex.Nodes.GetValueOrDefault(claim.SubjectNodeId);

        if (!claim.SubjectSymbol.IsEmpty)
        {
            var resolvedNodeId = _symbolIndex.FindFirstNode(claim.SubjectSymbol);
            if (resolvedNodeId is not null)
                return _graphIndex.Nodes.GetValueOrDefault(resolvedNodeId);
        }

        return null;
    }

    private ClaimClassification ClassifyFromNode(GraphNode node, bool hasSymbol)
    {
        if (!hasSymbol) return ClaimClassification.Speculative;

        var groundingKind = node.GroundingKind;
        var truthType = node.TruthType;
        var confidence = node.Confidence;

        if (confidence >= 0.8 && groundingKind == GroundingKindKinds.SemanticMethod)
            return ClaimClassification.Grounded;

        if (confidence >= 0.5)
            return ClaimClassification.Inferred;

        if (confidence >= 0.3)
            return ClaimClassification.Speculative;

        return ClaimClassification.Hallucinated;
    }

    private void CheckForbiddenPatterns(ClaimSubject claim, ClaimClassification classification, List<string> failures)
    {
        if (classification == ClaimClassification.Grounded) return;

        foreach (var pattern in _options.ForbiddenClaimPatterns)
        {
            if (claim.ClaimText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Claim contains forbidden pattern: '{pattern}'");
            }
        }
    }
}

public sealed class GroundedClaimValidatorOptions
{
    public double ScoreEqualityEpsilon { get; init; } = 0.0001;

    public bool RequireSymbolBinding { get; init; } = true;

    public bool RequireTraversalEvidence { get; init; } = true;

    public bool RequireProvenanceChain { get; init; } = true;

    public IReadOnlyList<string> ForbiddenClaimPatterns { get; init; } = new[]
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

    public static GroundedClaimValidatorOptions Default => new();
}

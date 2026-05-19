// =============================================================================
// Cognition/GroundedRootCauseExplorer.cs — evidence-backed root cause analysis
// =============================================================================
// Determinism: all execution reasoning is based on graph traversal, not speculation.
// Provenance: every diagnostic claim cites the specific nodes and paths.
// Replay: RootCauseResult is structurally comparable.
// Grounding: root cause explanations are derived from graph structure, execution
//   paths, and contradiction analysis — never from inference without evidence.
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Grounding.Confidence;
using Core.Grounding.Contradictions;
using Core.Semantics;

namespace Core.Cognition;

public sealed class GroundedRootCauseExplorer
{
    private readonly GraphQueryService _graphQuery;
    private readonly SymbolReferenceIndex _symbolIndex;
    private readonly RootCauseOptions _options;

    public GroundedRootCauseExplorer(
        GraphQueryService graphQuery,
        SymbolReferenceIndex symbolIndex,
        RootCauseOptions? options = null)
    {
        _graphQuery = graphQuery ?? throw new ArgumentNullException(nameof(graphQuery));
        _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
        _options = options ?? RootCauseOptions.Default;
    }

    public CognitionResult Explore(string query)
    {
        var resultId = $"rootcause-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var explanations = new List<GroundedExplanation>();
        var citations = new List<EvidenceReference>();
        var expId = 0;
        var citId = 0;

        var targets = ResolveRootCauseTargets(query);
        if (targets.Count == 0)
        {
            return BuildNoTargetResult(resultId, query, explanations, citations);
        }

        var failurePaths = AnalyzeFailurePaths(targets);
        var dependencyFailures = AnalyzeDependencyFailures(targets);
        var contradictionPaths = IdentifyContradictionPaths(targets);
        var rootCauseCandidates = SynthesizeRootCauses(targets, failurePaths, dependencyFailures, contradictionPaths);

        var overallConfidence = rootCauseCandidates.Count > 0
            ? rootCauseCandidates.Max(r => r.Confidence)
            : ConfidenceLevel.Weak;

        explanations.Add(DescribeDiagnosisOverview(targets, rootCauseCandidates.Count, ref expId));
        explanations.AddRange(DescribeRootCauseCandidates(rootCauseCandidates, ref expId, ref citId, citations));
        explanations.AddRange(DescribeFailurePaths(failurePaths, ref expId, ref citId, citations));
        explanations.AddRange(DescribeDependencyFailures(dependencyFailures, ref expId, ref citId, citations));
        explanations.AddRange(DescribeContradictionPaths(contradictionPaths, ref expId));

        return new CognitionResult
        {
            ResultId = resultId,
            Query = query,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ResultType = CognitionResultType.RootCauseAnalysis,
            Explanations = explanations,
            Citations = citations,
            OverallConfidence = overallConfidence,
        };
    }

    private List<string> ResolveRootCauseTargets(string query)
    {
        var keywords = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length > 2)
            .ToList();

        var allNodes = _graphQuery.GetAllNodes().ToList();
        var scored = new List<(string NodeId, int Score)>();

        foreach (var node in allNodes)
        {
            var score = 0;
            var searchText = $"{node.Label} {node.ClassName} {node.MethodName}".ToLowerInvariant();

            foreach (var kw in keywords)
            {
                if (searchText.Contains(kw, StringComparison.Ordinal))
                    score += 2;
            }

            if (node.Label.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || node.Label.Contains("retry", StringComparison.OrdinalIgnoreCase)
                || node.Label.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || node.Label.Contains("exception", StringComparison.OrdinalIgnoreCase)
                || node.Label.Contains("error", StringComparison.OrdinalIgnoreCase)
                || node.Label.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
                || node.Label.Contains("sync", StringComparison.OrdinalIgnoreCase)
                || node.Label.Contains("lock", StringComparison.OrdinalIgnoreCase))
                score += 1;

            if (score > 0)
                scored.Add((node.Id, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.NodeId, StringComparer.Ordinal)
            .Take(5)
            .Select(s => s.NodeId)
            .ToList();
    }

    private List<FailurePath> AnalyzeFailurePaths(List<string> targets)
    {
        var paths = new List<FailurePath>();

        foreach (var targetId in targets.OrderBy(id => id, StringComparer.Ordinal))
        {
            var node = _graphQuery.GetNode(targetId);
            if (node is null) continue;

            var callers = _graphQuery.GetCallers(targetId);
            var callees = _graphQuery.GetCallees(targetId);

            var nodePath = new List<string> { targetId };
            var expanded = false;

            foreach (var calleeId in callees.OrderBy(c => c, StringComparer.Ordinal).Take(3))
            {
                var callee = _graphQuery.GetNode(calleeId);
                if (callee is null) continue;

                nodePath.Add(calleeId);
                expanded = true;

                var subCallees = _graphQuery.GetCallees(calleeId);
                foreach (var subId in subCallees.OrderBy(s => s, StringComparer.Ordinal).Take(2))
                {
                    nodePath.Add(subId);
                }
            }

            if (expanded || callers.Count > 0)
            {
                paths.Add(new FailurePath
                {
                    RootNodeId = targetId,
                    RootNodeLabel = node.Label,
                    PathNodeIds = nodePath,
                    CallerCount = callers.Count,
                    CalleeCount = callees.Count,
                    Confidence = callers.Count > 0 ? ConfidenceLevel.Strong : ConfidenceLevel.Moderate,
                    SourceFile = node.SourceFile,
                });
            }
        }

        return paths;
    }

    private List<DependencyFailure> AnalyzeDependencyFailures(List<string> targets)
    {
        var failures = new List<DependencyFailure>();

        foreach (var targetId in targets.OrderBy(id => id, StringComparer.Ordinal))
        {
            var node = _graphQuery.GetNode(targetId);
            if (node is null) continue;

            var callees = _graphQuery.GetCallees(targetId);
            var externalCallees = new List<string>();

            foreach (var calleeId in callees.OrderBy(c => c, StringComparer.Ordinal))
            {
                var callee = _graphQuery.GetNode(calleeId);
                if (callee is not null && callee.IsExternal)
                {
                    externalCallees.Add(callee.Label);
                }
            }

            if (externalCallees.Count > 0)
            {
                failures.Add(new DependencyFailure
                {
                    SubjectNodeId = targetId,
                    SubjectLabel = node.Label,
                    ExternalDependencies = externalCallees,
                    DependencyCount = externalCallees.Count,
                    Confidence = ConfidenceLevel.Strong,
                });
            }
        }

        return failures;
    }

    private List<ContradictionPath> IdentifyContradictionPaths(List<string> targets)
    {
        var contradictions = new List<ContradictionPath>();

        foreach (var targetId in targets.OrderBy(id => id, StringComparer.Ordinal))
        {
            var node = _graphQuery.GetNode(targetId);
            if (node is null) continue;

            var callers = _graphQuery.GetCallers(targetId);

            foreach (var callerId in callers.OrderBy(c => c, StringComparer.Ordinal).Take(5))
            {
                var caller = _graphQuery.GetNode(callerId);
                if (caller is null) continue;

                var siblingCallees = _graphQuery.GetCallees(callerId)
                    .Where(c => c != targetId)
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToList();

                if (siblingCallees.Count > 0)
                {
                    var siblingLabels = siblingCallees
                        .Select(c => _graphQuery.GetNode(c)?.Label ?? c)
                        .ToList();

                    contradictions.Add(new ContradictionPath
                    {
                        PrimaryNodeId = targetId,
                        PrimaryLabel = node.Label,
                        SharedCallerId = callerId,
                        SharedCallerLabel = caller.Label,
                        SiblingCalleeIds = siblingCallees,
                        SiblingCalleeLabels = siblingLabels,
                        ConflictDescription = $"'{node.Label}' called alongside: {string.Join(", ", siblingLabels.Take(3))} — potential interaction failure point.",
                    });
                }
            }
        }

        return contradictions;
    }

    private List<RootCauseCandidate> SynthesizeRootCauses(
        List<string> targets,
        List<FailurePath> failurePaths,
        List<DependencyFailure> dependencyFailures,
        List<ContradictionPath> contradictionPaths)
    {
        var candidates = new List<RootCauseCandidate>();

        foreach (var target in targets.OrderBy(id => id, StringComparer.Ordinal))
        {
            var node = _graphQuery.GetNode(target);
            if (node is null) continue;

            var targetFailures = failurePaths.Where(p => p.RootNodeId == target).ToList();
            var targetDependencies = dependencyFailures.Where(d => d.SubjectNodeId == target).ToList();
            var targetContradictions = contradictionPaths.Where(c => c.PrimaryNodeId == target).ToList();

            var causes = new List<string>();
            var confidence = ConfidenceLevel.Moderate;

            if (targetFailures.Count > 0)
            {
                var fp = targetFailures[0];
                causes.Add($"{fp.RootNodeLabel} has {fp.CallerCount} caller(s) and {fp.CalleeCount} callee(s) — check the execution path through {string.Join(" → ", fp.PathNodeIds.Take(4).Select(ShortenId))}");
                confidence = fp.CallerCount > 2 ? ConfidenceLevel.Strong : ConfidenceLevel.Moderate;
            }

            if (targetDependencies.Count > 0)
            {
                var dep = targetDependencies[0];
                causes.Add($"Depends on external node(s): {string.Join(", ", dep.ExternalDependencies)} — external failure could propagate.");
                confidence = ConfidenceLevel.Strong;
            }

            if (targetContradictions.Count > 0)
            {
                var cp = targetContradictions[0];
                causes.Add($"Contradiction: {cp.ConflictDescription}");
                confidence = ConfidenceLevel.Moderate;
            }

            if (causes.Count == 0)
            {
                causes.Add($"Insufficient graph evidence for root cause of '{node.Label}'. Graph may lack runtime behavior instrumentation.");
                confidence = ConfidenceLevel.Weak;
            }

            foreach (var cause in causes)
            {
                candidates.Add(new RootCauseCandidate
                {
                    SubjectNodeId = target,
                    SubjectLabel = node.Label,
                    CauseDescription = cause,
                    Confidence = confidence,
                    SourceFile = node.SourceFile,
                });
            }
        }

        return candidates
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.SubjectLabel, StringComparer.Ordinal)
            .ToList();
    }

    private GroundedExplanation DescribeDiagnosisOverview(List<string> targets, int causeCount, ref int expId)
    {
        return new GroundedExplanation
        {
            ExplanationId = $"rca-exp-{expId++:D5}",
            Text = $"Diagnosis targets: {targets.Count} method(s). Root cause hypotheses: {causeCount}.",
            Claim = "Diagnosis overview",
            ConfidenceLevel = causeCount > 0 ? ConfidenceLevel.Strong : ConfidenceLevel.Weak,
            SupportingNodeIds = targets,
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        };
    }

    private IReadOnlyList<GroundedExplanation> DescribeRootCauseCandidates(
        List<RootCauseCandidate> candidates,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var cand in candidates.Take(10))
        {
            var citIds = new List<string>();
            AddRootCauseCitation(cand, citations, ref citId);
            citIds.Add($"cite-{citations.Last().CitationId}");

            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"rca-exp-{expId++:D5}",
                Text = $"[{cand.Confidence}] {cand.SubjectLabel}: {cand.CauseDescription}",
                Claim = $"Root cause: {cand.SubjectLabel}",
                ConfidenceLevel = cand.Confidence,
                SupportingNodeIds = new[] { cand.SubjectNodeId },
                SupportingSourceFiles = new[] { cand.SourceFile }.Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CitationIds = citIds,
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeFailurePaths(
        List<FailurePath> paths,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var path in paths
            .OrderByDescending(p => p.Confidence)
            .Take(8))
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"rca-exp-{expId++:D5}",
                Text = $"Execution path analysis: {path.RootNodeLabel} → {string.Join(" → ", path.PathNodeIds.Skip(1).Select(ShortenId))} (callers={path.CallerCount}, callees={path.CalleeCount})",
                Claim = $"Failure path: {path.RootNodeLabel}",
                ConfidenceLevel = path.Confidence,
                SupportingNodeIds = path.PathNodeIds,
                SupportingSourceFiles = new[] { path.SourceFile }.Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CitationIds = Array.Empty<string>(),
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeDependencyFailures(
        List<DependencyFailure> failures,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var failure in failures
            .OrderByDescending(f => f.Confidence)
            .ThenBy(f => f.SubjectLabel, StringComparer.Ordinal)
            .Take(5))
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"rca-exp-{expId++:D5}",
                Text = $"External dependency: {failure.SubjectLabel} calls {failure.DependencyCount} external node(s): {string.Join(", ", failure.ExternalDependencies.Take(3))}",
                Claim = $"External dependency: {failure.SubjectLabel}",
                ConfidenceLevel = failure.Confidence,
                SupportingNodeIds = new[] { failure.SubjectNodeId },
                SupportingSourceFiles = Array.Empty<string>(),
                CitationIds = Array.Empty<string>(),
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeContradictionPaths(
        List<ContradictionPath> contradictions,
        ref int expId)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var cp in contradictions
            .OrderByDescending(c => c.SiblingCalleeIds.Count)
            .Take(5))
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"rca-exp-{expId++:D5}",
                Text = $"Potential interaction failure: {cp.ConflictDescription}",
                Claim = "Interaction failure point",
                ConfidenceLevel = ConfidenceLevel.Moderate,
                SupportingNodeIds = new[] { cp.PrimaryNodeId, cp.SharedCallerId }
                    .Concat(cp.SiblingCalleeIds).Distinct(StringComparer.Ordinal).ToList(),
                SupportingSourceFiles = Array.Empty<string>(),
                CitationIds = Array.Empty<string>(),
            });
        }

        return explanations;
    }

    private void AddRootCauseCitation(RootCauseCandidate candidate, List<EvidenceReference> citations, ref int citId)
    {
        citations.Add(new EvidenceReference
        {
            CitationId = $"cite-{citId++:D5}",
            SourceNodeId = candidate.SubjectNodeId,
            SourceNodeLabel = candidate.SubjectLabel,
            SourceFile = candidate.SourceFile,
            SymbolHandle = "",
            ConfidenceLevel = candidate.Confidence,
        });
    }

    private CognitionResult BuildNoTargetResult(
        string resultId, string query,
        List<GroundedExplanation> explanations,
        List<EvidenceReference> citations)
    {
        explanations.Add(new GroundedExplanation
        {
            ExplanationId = "rca-exp-empty",
            Text = "No relevant code regions found for the query. Try describing specific symptoms, method names, or error messages.",
            Claim = query,
            ConfidenceLevel = ConfidenceLevel.Weak,
            SupportingNodeIds = Array.Empty<string>(),
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        });

        return new CognitionResult
        {
            ResultId = resultId,
            Query = query,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ResultType = CognitionResultType.RootCauseAnalysis,
            Explanations = explanations,
            Citations = citations,
            OverallConfidence = ConfidenceLevel.Weak,
        };
    }

    private static string ShortenId(string id)
    {
        return id.Contains("::") ? id[(id.LastIndexOf("::") + 2)..] : id;
    }
}

public class RootCauseOptions
{
    public int MaxTargets { get; init; } = 5;
    public int MaxPaths { get; init; } = 30;
    public int MaxRootCauseCandidates { get; init; } = 10;

    public static RootCauseOptions Default => new();
}

public sealed class FailurePath
{
    public required string RootNodeId { get; init; }
    public required string RootNodeLabel { get; init; }
    public required IReadOnlyList<string> PathNodeIds { get; init; }
    public int CallerCount { get; init; }
    public int CalleeCount { get; init; }
    public ConfidenceLevel Confidence { get; init; }
    public string SourceFile { get; init; } = "";
}

public sealed class DependencyFailure
{
    public required string SubjectNodeId { get; init; }
    public required string SubjectLabel { get; init; }
    public required IReadOnlyList<string> ExternalDependencies { get; init; }
    public int DependencyCount { get; init; }
    public ConfidenceLevel Confidence { get; init; }
}

public sealed class ContradictionPath
{
    public required string PrimaryNodeId { get; init; }
    public required string PrimaryLabel { get; init; }
    public required string SharedCallerId { get; init; }
    public required string SharedCallerLabel { get; init; }
    public required IReadOnlyList<string> SiblingCalleeIds { get; init; }
    public required IReadOnlyList<string> SiblingCalleeLabels { get; init; }
    public required string ConflictDescription { get; init; }
}

public sealed class RootCauseCandidate
{
    public required string SubjectNodeId { get; init; }
    public required string SubjectLabel { get; init; }
    public required string CauseDescription { get; init; }
    public ConfidenceLevel Confidence { get; init; }
    public string SourceFile { get; init; } = "";
}

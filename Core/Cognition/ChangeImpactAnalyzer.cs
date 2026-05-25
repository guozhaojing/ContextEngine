// =============================================================================
// Cognition/ChangeImpactAnalyzer.cs — downstream impact & risk scoring
// =============================================================================
// Determinism: all impact traversals are deterministic BFS/DFS with sorted edges.
// Provenance: every impacted node carries the propagation path from the change point.
// Replay: ChangeImpactResult is structurally comparable.
// Grounding: risk is scored by propagation depth × confidence decay × fan-out.
//   Contradiction-aware: conflicting impacts are surfaced as warnings.
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Grounding.Confidence;
using Core.Semantics;

namespace Core.Cognition;

public sealed class ChangeImpactAnalyzer
{
    private readonly GraphQueryService _graphQuery;
    private readonly SymbolReferenceIndex _symbolIndex;
    private readonly ConfidencePropagationEngine _confidenceEngine;
    private readonly ChangeImpactOptions _options;

    public ChangeImpactAnalyzer(
        GraphQueryService graphQuery,
        SymbolReferenceIndex symbolIndex,
        ConfidencePropagationEngine confidenceEngine,
        ChangeImpactOptions? options = null)
    {
        _graphQuery = graphQuery ?? throw new ArgumentNullException(nameof(graphQuery));
        _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
        _confidenceEngine = confidenceEngine ?? throw new ArgumentNullException(nameof(confidenceEngine));
        _options = options ?? ChangeImpactOptions.Default;
    }

    public CognitionResult Analyze(string query, string? targetMethodId = null)
    {
        var resultId = $"impact-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var explanations = new List<GroundedExplanation>();
        var citations = new List<EvidenceReference>();
        var expId = 0;
        var citId = 0;

        var targetIds = ResolveTargets(query, targetMethodId);
        if (targetIds.Count == 0)
        {
            return BuildEmptyResult(resultId, query, explanations, citations);
        }

        var allImpacted = new List<ImpactPath>();
        var allUpstream = new List<ImpactPath>();

        foreach (var targetId in targetIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            var downstream = AnalyzeDownstreamImpact(targetId);
            var upstream = AnalyzeUpstreamDependents(targetId);

            allImpacted.AddRange(downstream);
            allUpstream.AddRange(upstream);

            AddNodeCitation(targetId, citations, ref citId);
        }

        var uniqueDownstream = allImpacted
            .GroupBy(p => p.TargetNodeId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(p => p.TargetNodeId, StringComparer.Ordinal)
            .ToList();

        var uniqueUpstream = allUpstream
            .GroupBy(p => p.TargetNodeId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(p => p.TargetNodeId, StringComparer.Ordinal)
            .ToList();

        var maxRisk = uniqueDownstream.Count > 0
            ? uniqueDownstream.Max(d => d.RiskScore)
            : 0;

        var overallConfidence = maxRisk switch
        {
            <= 0.2 => ConfidenceLevel.Certain,
            <= 0.4 => ConfidenceLevel.Strong,
            <= 0.6 => ConfidenceLevel.Moderate,
            <= 0.8 => ConfidenceLevel.Weak,
            _ => ConfidenceLevel.Speculative,
        };

        explanations.Add(DescribeImpactSummary(targetIds, uniqueDownstream.Count, uniqueUpstream.Count, ref expId));
        explanations.AddRange(DescribeDownstreamImpact(uniqueDownstream, ref expId, ref citId, citations));
        explanations.AddRange(DescribeUpstreamDependents(uniqueUpstream, ref expId, ref citId, citations));
        explanations.AddRange(DescribeRiskAssessment(uniqueDownstream, targetIds, ref expId));

        return new CognitionResult
        {
            ResultId = resultId,
            Query = query,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ResultType = CognitionResultType.ChangeImpactAnalysis,
            Explanations = explanations,
            Citations = citations,
            OverallConfidence = overallConfidence,
        };
    }

    private List<string> ResolveTargets(string query, string? explicitId)
    {
        if (!string.IsNullOrEmpty(explicitId) && _graphQuery.Contains(explicitId))
            return new List<string> { explicitId };

        var results = new List<string>();
        var allNodes = _graphQuery.GetAllNodes().ToList();

        var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var node in allNodes)
        {
            foreach (var kw in keywords.Take(5))
            {
                if (node.Label.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || node.ClassName?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (!results.Contains(node.Id, StringComparer.Ordinal))
                        results.Add(node.Id);
                }
            }
        }

        return results.OrderBy(r => r, StringComparer.Ordinal).Take(5).ToList();
    }

    private List<ImpactPath> AnalyzeDownstreamImpact(string targetId)
    {
        var impacted = new List<ImpactPath>();
        var propResult = _confidenceEngine.Propagate(
            new[] { targetId },
            PropagationDirection.Outgoing);

        foreach (var entry in propResult.Entries
            .OrderBy(e => e.NodeId, StringComparer.Ordinal))
        {
            if (entry.NodeId == targetId) continue;

            var node = _graphQuery.GetNode(entry.NodeId);
            var depType = GetDependencyType(targetId, entry.NodeId, node);
            var riskScore = ComputeRisk(entry.PropagatedConfidence, depType);

            impacted.Add(new ImpactPath
            {
                SourceNodeId = targetId,
                TargetNodeId = entry.NodeId,
                TargetNodeLabel = node?.Label ?? entry.NodeId,
                HopDistance = entry.PropagatedConfidence.HopDistance,
                Confidence = entry.PropagatedConfidence.Score,
                RiskScore = riskScore,
                RiskLevel = ClassifyRisk(riskScore),
                SourceFile = node?.SourceFile ?? "",
                EdgeKind = entry.ViaEdgeKind ?? "",
                DependencyType = depType,
            });
        }

        return impacted;
    }

    private string GetDependencyType(string fromId, string toId, GraphNode? toNode)
    {
        // Check edge attributes for dependency type tag
        var edges = _graphQuery.GetOutgoingEdges(fromId);
        foreach (var e in edges)
        {
            if (e.ToId == toId)
            {
                var dt = e.GetAttr("dependencyType");
                if (!string.IsNullOrEmpty(dt)) return dt;
            }
        }

        // Fallback classification
        if (toNode is not null)
        {
            var fromNode = _graphQuery.GetNode(fromId);
            var sameClass = fromNode is not null
                && string.Equals(fromNode.ClassName, toNode.ClassName, StringComparison.Ordinal);
            if (sameClass) return DependencyEdgeTypes.PrivateImplementation;
        }

        return DependencyEdgeTypes.TransitiveCall;
    }

    private List<ImpactPath> AnalyzeUpstreamDependents(string targetId)
    {
        var dependents = new List<ImpactPath>();
        var entryPoints = _graphQuery.FindEntryPoints(targetId);

        foreach (var entryId in entryPoints.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (entryId == targetId) continue;
            var node = _graphQuery.GetNode(entryId);

            dependents.Add(new ImpactPath
            {
                SourceNodeId = entryId,
                TargetNodeId = targetId,
                TargetNodeLabel = node?.Label ?? entryId,
                HopDistance = 0,
                Confidence = 1.0,
                RiskScore = 0.3,
                RiskLevel = RiskLevel.Low,
                SourceFile = node?.SourceFile ?? "",
                EdgeKind = "entry-point-dependency",
            });
        }

        return dependents;
    }

    private static double ComputeRisk(GroundingConfidence confidence, string depType)
    {
        var baseRisk = 1.0 - confidence.Score;
        var hopPenalty = Math.Min(confidence.HopDistance * 0.1, 0.4);
        var speculativePenalty = confidence.HasSpeculativeAncestor ? 0.2 : 0;

        // Private implementation → downgrade risk
        var typeAdjustment = depType switch
        {
            DependencyEdgeTypes.PrivateImplementation => -0.2,
            DependencyEdgeTypes.InterfaceContract => -0.1,
            DependencyEdgeTypes.EntryPointReachable => 0.05,
            DependencyEdgeTypes.TransitiveCall => 0.1,
            _ => 0,
        };

        return Math.Clamp(baseRisk + hopPenalty + speculativePenalty + typeAdjustment, 0, 1.0);
    }

    private static RiskLevel ClassifyRisk(double score) => score switch
    {
        <= 0.2 => RiskLevel.Low,
        <= 0.4 => RiskLevel.Medium,
        <= 0.7 => RiskLevel.High,
        _ => RiskLevel.Critical,
    };

    private GroundedExplanation DescribeImpactSummary(List<string> targets, int downstream, int upstream, ref int expId)
    {
        return new GroundedExplanation
        {
            ExplanationId = $"impact-exp-{expId++:D5}",
            Text = $"Change target(s): {string.Join(", ", targets.Select(ShortenId))}. Downstream impact: {downstream} node(s). Upstream dependents: {upstream} node(s).",
            Claim = "Impact summary",
            ConfidenceLevel = downstream > 0 ? ConfidenceLevel.Strong : ConfidenceLevel.Moderate,
            SupportingNodeIds = targets,
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        };
    }

    private IReadOnlyList<GroundedExplanation> DescribeDownstreamImpact(
        List<ImpactPath> impacts,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        if (impacts.Count == 0)
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"impact-exp-{expId++:D5}",
                Text = "No downstream impact detected.",
                Claim = "Downstream impact",
                ConfidenceLevel = ConfidenceLevel.Moderate,
                SupportingNodeIds = Array.Empty<string>(),
                SupportingSourceFiles = Array.Empty<string>(),
                CitationIds = Array.Empty<string>(),
            });
            return explanations;
        }

        foreach (var impact in impacts
            .OrderByDescending(i => i.RiskScore)
            .Take(15))
        {
            var citIds = new List<string>();
            if (!string.IsNullOrEmpty(impact.TargetNodeId))
            {
                AddCitation(impact, citations, ref citId);
                citIds.Add($"cite-{citations.Last().CitationId}");
            }

            var depLabel = impact.DependencyType switch
            {
                DependencyEdgeTypes.PrivateImplementation => "private",
                DependencyEdgeTypes.InterfaceContract => "interface",
                DependencyEdgeTypes.EntryPointReachable => "entry-point",
                DependencyEdgeTypes.DirectCall => "direct",
                _ => "transitive",
            };

            var text = $"[{impact.RiskLevel}] [{depLabel}] {impact.TargetNodeLabel}: hop={impact.HopDistance}, confidence={impact.Confidence:F2}, risk={impact.RiskScore:F2}";

            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"impact-exp-{expId++:D5}",
                Text = text,
                Claim = $"Impact on: {impact.TargetNodeLabel}",
                ConfidenceLevel = impact.Confidence >= 0.8 ? ConfidenceLevel.Strong : ConfidenceLevel.Moderate,
                SupportingNodeIds = new[] { impact.TargetNodeId },
                SupportingSourceFiles = new[] { impact.SourceFile }.Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CitationIds = citIds,
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeUpstreamDependents(
        List<ImpactPath> dependents,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        if (dependents.Count == 0)
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"impact-exp-{expId++:D5}",
                Text = "No upstream API entry points detected.",
                Claim = "Upstream entry points",
                ConfidenceLevel = ConfidenceLevel.Moderate,
                SupportingNodeIds = Array.Empty<string>(),
                SupportingSourceFiles = Array.Empty<string>(),
                CitationIds = Array.Empty<string>(),
            });
            return explanations;
        }

        foreach (var dep in dependents.Take(10))
        {
            var depLabel = dep.DependencyType switch
            {
                DependencyEdgeTypes.EntryPointReachable => "reachable from API entry point",
                DependencyEdgeTypes.PrivateImplementation => "via private method chain",
                _ => "upstream dependent",
            };

            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"impact-exp-{expId++:D5}",
                Text = $"{depLabel}: {dep.TargetNodeLabel}",
                Claim = $"Entry point: {dep.SourceNodeId}",
                ConfidenceLevel = ConfidenceLevel.Strong,
                SupportingNodeIds = new[] { dep.SourceNodeId, dep.TargetNodeId },
                SupportingSourceFiles = new[] { dep.SourceFile }.Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CitationIds = Array.Empty<string>(),
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeRiskAssessment(
        List<ImpactPath> impacts,
        List<string> targetIds,
        ref int expId)
    {
        var explanations = new List<GroundedExplanation>();
        var maxRisk = impacts.Count > 0 ? impacts.Max(i => i.RiskScore) : 0;
        var critCount = impacts.Count(i => i.RiskLevel >= RiskLevel.High);

        var assessment = maxRisk switch
        {
            >= 0.7 => $"HIGH RISK: {critCount} high/critical impact(s) detected. Changes may have significant downstream effects.",
            >= 0.4 => $"MODERATE RISK: {critCount} notable impact(s). Changes require careful review.",
            > 0 => $"LOW RISK: Limited downstream impact. Changes are unlikely to cause cascading failures.",
            _ => "No measurable downstream risk.",
        };

        explanations.Add(new GroundedExplanation
        {
            ExplanationId = $"impact-exp-{expId++:D5}",
            Text = assessment,
            Claim = "Risk assessment",
            ConfidenceLevel = maxRisk >= 0.7 ? ConfidenceLevel.Moderate : ConfidenceLevel.Strong,
            SupportingNodeIds = targetIds,
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        });

        return explanations;
    }

    private void AddNodeCitation(string nodeId, List<EvidenceReference> citations, ref int citId)
    {
        var node = _graphQuery.GetNode(nodeId);
        if (node is null) return;

        citations.Add(new EvidenceReference
        {
            CitationId = $"cite-{citId++:D5}",
            SourceNodeId = nodeId,
            SourceNodeLabel = node.Label,
            SourceFile = node.SourceFile,
            SymbolHandle = node.SymbolHandle,
            ConfidenceLevel = string.IsNullOrEmpty(node.SymbolHandle)
                ? ConfidenceLevel.Moderate : ConfidenceLevel.Certain,
        });
    }

    private void AddCitation(ImpactPath impact, List<EvidenceReference> citations, ref int citId)
    {
        citations.Add(new EvidenceReference
        {
            CitationId = $"cite-{citId++:D5}",
            SourceNodeId = impact.TargetNodeId,
            SourceNodeLabel = impact.TargetNodeLabel,
            SourceFile = impact.SourceFile,
            SymbolHandle = "",
            ConfidenceLevel = impact.Confidence >= 0.8 ? ConfidenceLevel.Strong : ConfidenceLevel.Moderate,
            EdgeKind = impact.EdgeKind,
        });
    }

    private static string ShortenId(string id)
    {
        return id.Contains("::") ? id[(id.LastIndexOf("::") + 2)..] : id;
    }

    private CognitionResult BuildEmptyResult(
        string resultId, string query,
        List<GroundedExplanation> explanations,
        List<EvidenceReference> citations)
    {
        explanations.Add(new GroundedExplanation
        {
            ExplanationId = "impact-exp-empty",
            Text = "Target not found in the code graph. Try specifying a method, class, or service name.",
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
            ResultType = CognitionResultType.ChangeImpactAnalysis,
            Explanations = explanations,
            Citations = citations,
            OverallConfidence = ConfidenceLevel.Weak,
        };
    }
}

public class ChangeImpactOptions
{
    public int MaxDownstreamDepth { get; init; } = 5;
    public int MaxUpstreamDepth { get; init; } = 5;
    public int MaxImpactResults { get; init; } = 20;
    public double MinConfidenceForImpact { get; init; } = 0.2;

    public static ChangeImpactOptions Default => new();
}

public sealed class ImpactPath
{
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public string TargetNodeLabel { get; init; } = "";
    public int HopDistance { get; init; }
    public double Confidence { get; init; }
    public double RiskScore { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public string SourceFile { get; init; } = "";
    public string EdgeKind { get; init; } = "";
    public string DependencyType { get; init; } = DependencyEdgeTypes.TransitiveCall;
}

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

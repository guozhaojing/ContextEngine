// =============================================================================
// Explainability/PathEvidenceReport.cs — evidence report for a traversal path
// =============================================================================
// Documents the full evidence chain for a selected semantic path:
//   - Each hop with edge kind, confidence, grounding status
//   - Source files and symbols at each node
//   - Truth scores and propagation depth
//   - Why this path was selected over alternatives
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Semantics;
using Core.Truth;

namespace Core.Explainability;

public sealed class PathEvidenceReport
{
    public required string PathId { get; init; }
    public required string PathSummary { get; init; }
    public int HopCount { get; init; }
    public required IReadOnlyList<HopEvidence> Hops { get; init; }

    public double AverageConfidence =>
        Hops.Count > 0 ? Hops.Average(h => h.EdgeConfidence) : 0;

    public int GroundedHops => Hops.Count(h => h.IsGrounded);

    public double GroundingRatio =>
        HopCount > 0 ? (double)GroundedHops / HopCount : 0;

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Path Evidence: {PathSummary}");
        sb.AppendLine($"Hops: {HopCount} | Avg Confidence: {AverageConfidence:F2} | Grounded: {GroundedHops}/{HopCount}");
        sb.AppendLine();

        for (var i = 0; i < Hops.Count; i++)
        {
            var hop = Hops[i];
            sb.AppendLine($"Hop {i}: {hop.NodeLabel} [{hop.NodeKind}]");
            sb.AppendLine($"  Source: {hop.SourceFile}");
            sb.AppendLine($"  Symbol: {hop.SymbolHandle}");
            sb.AppendLine($"  Edge: {hop.EdgeKind} (confidence={hop.EdgeConfidence:F2}, grounded={hop.IsGrounded})");
            sb.AppendLine($"  Truth: {hop.TruthType} depth={hop.PropagationDepth}");
            sb.AppendLine($"  Evidence: {hop.Evidence}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static PathEvidenceReport FromSemanticPath(
        SemanticPath path,
        GraphQueryService query,
        GraphIndex index)
    {
        var hops = new List<HopEvidence>();

        for (var i = 0; i < path.NodeIds.Count; i++)
        {
            var nodeId = path.NodeIds[i];
            var node = query.GetNode(nodeId);

            var edgeKind = i < path.EdgeKinds.Count ? path.EdgeKinds[i] : "";
            var edgeConfidence = 0.0;
            var edgeEvidence = "";
            var propagationDepth = 0;
            var grounded = false;

            if (i > 0 && i - 1 < path.EdgeKinds.Count)
            {
                var prevNodeId = path.NodeIds[i - 1];
                var outEdges = index.EdgeIdx.OutgoingByKind;
                if (outEdges.TryGetValue(prevNodeId, out var edges))
                {
                    var edge = edges.FirstOrDefault(e =>
                        StringComparer.Ordinal.Equals(e.ToId, nodeId)
                        && StringComparer.Ordinal.Equals(e.Kind, path.EdgeKinds[i - 1]));

                    if (!string.IsNullOrEmpty(edge.Kind))
                    {
                        var score = EdgeConfidenceCalculator.Calculate(edge, query.GetNode(prevNodeId), node);
                        edgeConfidence = score.Value;
                        edgeEvidence = score.Evidence.ToString();
                        propagationDepth = score.PropagationDepth;
                        grounded = score.IsGrounded;
                    }
                }
            }

            hops.Add(new HopEvidence
            {
                NodeId = nodeId,
                NodeLabel = node?.Label ?? nodeId,
                NodeKind = node?.Kind ?? "unknown",
                SourceFile = node?.SourceFile ?? "",
                SymbolHandle = node?.SymbolHandle ?? "",
                EdgeKind = edgeKind,
                EdgeConfidence = edgeConfidence,
                Evidence = edgeEvidence,
                TruthType = node?.TruthType ?? "unknown",
                PropagationDepth = propagationDepth,
                IsGrounded = grounded,
            });
        }

        return new PathEvidenceReport
        {
            PathId = path.PathId,
            PathSummary = path.Summary,
            HopCount = path.NodeIds.Count,
            Hops = hops,
        };
    }
}

public sealed class HopEvidence
{
    public required string NodeId { get; init; }
    public required string NodeLabel { get; init; }
    public required string NodeKind { get; init; }
    public string SourceFile { get; init; } = "";
    public string SymbolHandle { get; init; } = "";
    public string EdgeKind { get; init; } = "";
    public double EdgeConfidence { get; init; }
    public string Evidence { get; init; } = "";
    public string TruthType { get; init; } = "";
    public int PropagationDepth { get; init; }
    public bool IsGrounded { get; init; }
}

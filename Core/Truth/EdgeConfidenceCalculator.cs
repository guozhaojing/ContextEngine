// =============================================================================
// Truth/EdgeConfidenceCalculator.cs — computes confidence for every graph edge
// =============================================================================
// Determines TruthScore per edge based on:
//   - Edge source (Roslyn call vs analyzer-framework edge)
//   - Whether both endpoints are symbol-grounded
//   - Whether the edge was resolved via SemanticModel
//   - Propagation depth from the nearest source-fact edge
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Semantics;

namespace Core.Truth;

public static class EdgeConfidenceCalculator
{
    public static TruthScore Calculate(
        GraphEdge edge,
        GraphNode? fromNode,
        GraphNode? toNode,
        EdgeEvidence? overrideEvidence = null)
    {
        var evidence = overrideEvidence ?? InferEvidence(edge);
        var source = InferSource(edge);

        double baseScore = evidence switch
        {
            EdgeEvidence.RoslynSemanticCall => 1.0,
            EdgeEvidence.RoslynSyntaxCall => 0.8,
            EdgeEvidence.AnalyzerExact => 0.9,
            EdgeEvidence.AnalyzerHigh => 0.75,
            EdgeEvidence.AnalyzerMedium => 0.55,
            EdgeEvidence.AnalyzerLow => 0.3,
            EdgeEvidence.SyntaxPattern => 0.25,
            EdgeEvidence.Inferred => 0.15,
            _ => 0.1
        };

        var grounded = AreEndpointsGrounded(fromNode, toNode);
        if (!grounded)
            baseScore *= 0.5;

        return new TruthScore(
            baseScore,
            EvidenceFromEdge(evidence),
            source,
            0,
            grounded
        );
    }

    public static TruthScore Calculate(
        EdgeInfo edge,
        GraphNode? fromNode = null,
        GraphNode? toNode = null)
    {
        var evidenceStr = edge.GetAttr("evidence");
        var evidence = !string.IsNullOrEmpty(evidenceStr)
            ? ParseEdgeEvidence(evidenceStr)
            : InferEdgeEvidenceFromKind(edge.Kind);

        var source = InferSourceFromKind(edge.Kind);

        double baseScore = evidence switch
        {
            EdgeEvidence.RoslynSemanticCall => 1.0,
            EdgeEvidence.RoslynSyntaxCall => 0.8,
            EdgeEvidence.AnalyzerExact => 0.9,
            EdgeEvidence.AnalyzerHigh => 0.75,
            EdgeEvidence.AnalyzerMedium => 0.55,
            EdgeEvidence.AnalyzerLow => 0.3,
            EdgeEvidence.SyntaxPattern => 0.25,
            EdgeEvidence.Inferred => 0.15,
            _ => 0.1
        };

        if (fromNode is not null && toNode is not null)
        {
            var grounded = AreEndpointsGrounded(fromNode, toNode);
            if (!grounded)
                baseScore *= 0.5;
        }

        var depthStr = edge.GetAttr("propagationDepth");
        var depth = int.TryParse(depthStr, out var d) ? d : 0;

        return new TruthScore(
            baseScore,
            EvidenceFromEdge(evidence),
            source,
            depth,
            true
        );
    }

    private static EdgeEvidence InferEvidence(GraphEdge edge)
    {
        if (edge.Attributes.TryGetValue("evidence", out var evidenceStr))
            return ParseEdgeEvidence(evidenceStr);

        return edge.Kind switch
        {
            GraphEdgeKinds.Call when edge.IsResolved => EdgeEvidence.RoslynSyntaxCall,
            GraphEdgeKinds.Call => EdgeEvidence.Inferred,
            "nh:entity-access" => EdgeEvidence.AnalyzerHigh,
            "spring:implements" => EdgeEvidence.AnalyzerMedium,
            "spring:property-ref" => EdgeEvidence.AnalyzerLow,
            _ => EdgeEvidence.AnalyzerMedium
        };
    }

    private static TruthSource InferSource(GraphEdge edge)
    {
        if (edge.Attributes.TryGetValue("source", out var sourceStr))
            return sourceStr.ToLowerInvariant() switch
            {
                "roslyn" => TruthSource.Roslyn,
                "nhibernate" => TruthSource.NHibernate,
                "spring" => TruthSource.SpringNet,
                "inferred" => TruthSource.AnalyzerInferred,
                _ => TruthSource.Unknown
            };

        return edge.Kind switch
        {
            GraphEdgeKinds.Call => TruthSource.Roslyn,
            "nh:entity-access" => TruthSource.NHibernate,
            "spring:implements" or "spring:property-ref" => TruthSource.SpringNet,
            _ => TruthSource.AnalyzerInferred
        };
    }

    private static TruthSource InferSourceFromKind(string kind)
    {
        return kind switch
        {
            GraphEdgeKinds.Call => TruthSource.Roslyn,
            "nh:entity-access" => TruthSource.NHibernate,
            "spring:implements" or "spring:property-ref" => TruthSource.SpringNet,
            _ => TruthSource.AnalyzerInferred
        };
    }

    private static EdgeEvidence InferEdgeEvidenceFromKind(string kind)
    {
        return kind switch
        {
            GraphEdgeKinds.Call => EdgeEvidence.RoslynSyntaxCall,
            "nh:entity-access" or "spring:implements" => EdgeEvidence.AnalyzerHigh,
            "spring:property-ref" => EdgeEvidence.AnalyzerMedium,
            _ => EdgeEvidence.AnalyzerMedium
        };
    }

    private static EdgeEvidence ParseEdgeEvidence(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "roslyn-semantic" => EdgeEvidence.RoslynSemanticCall,
            "roslyn-syntax" => EdgeEvidence.RoslynSyntaxCall,
            "analyzer-exact" => EdgeEvidence.AnalyzerExact,
            "analyzer-high" => EdgeEvidence.AnalyzerHigh,
            "analyzer-medium" => EdgeEvidence.AnalyzerMedium,
            "analyzer-low" => EdgeEvidence.AnalyzerLow,
            "syntax-pattern" => EdgeEvidence.SyntaxPattern,
            "inferred" => EdgeEvidence.Inferred,
            _ => EdgeEvidence.Inferred
        };
    }

    private static EvidenceStrength EvidenceFromEdge(EdgeEvidence evidence)
    {
        return evidence switch
        {
            EdgeEvidence.RoslynSemanticCall => EvidenceStrength.SemanticDirect,
            EdgeEvidence.RoslynSyntaxCall or EdgeEvidence.AnalyzerExact => EvidenceStrength.SemanticInferred,
            EdgeEvidence.AnalyzerHigh => EvidenceStrength.SyntaxDirect,
            EdgeEvidence.AnalyzerMedium or EdgeEvidence.AnalyzerLow => EvidenceStrength.SyntaxPattern,
            _ => EvidenceStrength.None
        };
    }

    private static bool AreEndpointsGrounded(GraphNode? fromNode, GraphNode? toNode)
    {
        var hasSymbol1 = !string.IsNullOrEmpty(fromNode?.Attributes.GetValueOrDefault("symbolHandle", ""));
        if (fromNode is not null && fromNode.IsExternal)
            hasSymbol1 = true;

        var hasSymbol2 = !string.IsNullOrEmpty(toNode?.Attributes.GetValueOrDefault("symbolHandle", ""));
        if (toNode is not null && toNode.IsExternal)
            hasSymbol2 = true;

        return hasSymbol1 || hasSymbol2;
    }
}

public enum EdgeEvidence
{
    Inferred = 0,
    SyntaxPattern = 1,
    AnalyzerLow = 2,
    AnalyzerMedium = 3,
    AnalyzerHigh = 4,
    AnalyzerExact = 5,
    RoslynSyntaxCall = 6,
    RoslynSemanticCall = 7,
}

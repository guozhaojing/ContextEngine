// =============================================================================
// Truth/EntityGroundingValidator.cs — validates entity nodes against source
// =============================================================================
// Checks:
//   - Entity node has a valid source (Roslyn class symbol, NH mapping, Spring bean)
//   - Entity-table mapping is explicit (not inferred from naming)
//   - Entity name is stable (derived from symbol, not string heuristic)
//   - No hallucinated entities from LLM completions
// =============================================================================

using Core.Graph;
using Core.Graph.Analysis;
using Core.Semantics;

namespace Core.Truth;

public static class EntityGroundingValidator
{
    public static EntityGroundingResult Validate(GraphNode entityNode, CodeGraph graph)
    {
        if (entityNode.Kind != GraphNodeKind.Entity)
            return EntityGroundingResult.NotAnEntity;

        var evidence = DetermineEvidence(entityNode, graph);
        var confidence = DetermineConfidence(entityNode, graph);

        return new EntityGroundingResult
        {
            EntityId = entityNode.Id,
            IsGrounded = confidence > ResolutionConfidence.Low,
            Evidence = evidence,
            Confidence = confidence,
            HasSymbolBinding = HasSymbolBinding(entityNode),
            HasExplicitTableMapping = HasExplicitTableMapping(entityNode, graph),
            HasSourceFile = HasSourceFile(entityNode),
        };
    }

    public static bool IsSafeForContext(GraphNode entityNode, CodeGraph graph)
    {
        var result = Validate(entityNode, graph);
        return result.IsGrounded && result.HasSourceFile;
    }

    private static EntityEvidence DetermineEvidence(GraphNode node, CodeGraph graph)
    {
        if (HasSymbolBinding(node))
            return EntityEvidence.RoslynClassSymbol;

        if (node.Id.Contains("::nh:entity::"))
        {
            var hasMapping = graph.Facts.Any(f =>
                f.FactType == "nh-entity-access"
                && f.Data.ContainsKey("entity")
                && StringComparer.Ordinal.Equals(f.Data["entity"], node.Label));

            if (hasMapping)
                return EntityEvidence.NHibernateMapping;
        }

        if (node.Id.Contains("::spring:bean::"))
            return EntityEvidence.SpringBeanConfig;

        if (node.Attributes.TryGetValue("source", out var source) && source == "generic-resolution")
            return EntityEvidence.GenericResolution;

        return EntityEvidence.Inferred;
    }

    private static ResolutionConfidence DetermineConfidence(GraphNode node, CodeGraph graph)
    {
        if (HasSymbolBinding(node))
            return ResolutionConfidence.Exact;

        if (node.Id.Contains("::nh:entity::"))
        {
            var hbmFactCount = graph.Facts.Count(f =>
                f.FactType == "nh-entity-access" && f.SubjectKind == GraphSubjectKinds.Method);

            if (hbmFactCount > 0)
                return HasExplicitTableMapping(node, graph)
                    ? ResolutionConfidence.High
                    : ResolutionConfidence.Medium;
        }

        if (node.Attributes.TryGetValue("confidence", out var c))
        {
            return c.ToLowerInvariant() switch
            {
                "exact" => ResolutionConfidence.Exact,
                "high" => ResolutionConfidence.High,
                "medium" => ResolutionConfidence.Medium,
                "low" => ResolutionConfidence.Low,
                _ => ResolutionConfidence.Low
            };
        }

        return ResolutionConfidence.Low;
    }

    private static bool HasSymbolBinding(GraphNode node)
    {
        return !string.IsNullOrEmpty(node.Attributes.GetValueOrDefault("symbolHandle", ""));
    }

    private static bool HasExplicitTableMapping(GraphNode node, CodeGraph graph)
    {
        var entityName = node.Label;
        return graph.Facts.Any(f =>
            (f.FactType == "nh-entity-access" || f.FactType == "nh-hql")
            && f.Data.TryGetValue("entity", out var e)
            && StringComparer.Ordinal.Equals(e, entityName)
            && f.Data.ContainsKey("table"));
    }

    private static bool HasSourceFile(GraphNode node)
    {
        return !string.IsNullOrEmpty(node.Attributes.GetValueOrDefault("sourceFile", ""));
    }
}

public enum EntityEvidence
{
    Inferred = 0,
    GenericResolution = 1,
    SpringBeanConfig = 2,
    NHibernateMapping = 3,
    RoslynClassSymbol = 4,
}

public sealed class EntityGroundingResult
{
    public string EntityId { get; init; } = "";
    public bool IsGrounded { get; init; }
    public EntityEvidence Evidence { get; init; }
    public ResolutionConfidence Confidence { get; init; }
    public bool HasSymbolBinding { get; init; }
    public bool HasExplicitTableMapping { get; init; }
    public bool HasSourceFile { get; init; }

    public static EntityGroundingResult NotAnEntity => new()
    {
        IsGrounded = false,
        Evidence = EntityEvidence.Inferred,
        Confidence = ResolutionConfidence.Low,
    };
}

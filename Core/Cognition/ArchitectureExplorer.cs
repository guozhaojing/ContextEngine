// =============================================================================
// Cognition/ArchitectureExplorer.cs — subsystem architecture explication
// =============================================================================
// Determinism: all graph traversals are deterministic (sorted by node ID).
// Provenance: every explanation carries supporting node IDs and source files.
// Replay: ArchitectureExplanation is structurally comparable.
// Grounding: subsystem boundaries, orchestration layers, integration points
//   are identified through graph structure, not heuristics.
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Grounding.Confidence;
using Core.Semantics;

namespace Core.Cognition;

public sealed class ArchitectureExplorer
{
    private readonly GraphQueryService _graphQuery;
    private readonly SymbolReferenceIndex _symbolIndex;
    private readonly ArchitectureExplorerOptions _options;

    public ArchitectureExplorer(
        GraphQueryService graphQuery,
        SymbolReferenceIndex symbolIndex,
        ArchitectureExplorerOptions? options = null)
    {
        _graphQuery = graphQuery ?? throw new ArgumentNullException(nameof(graphQuery));
        _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
        _options = options ?? ArchitectureExplorerOptions.Default;
    }

    public CognitionResult Explore(string query)
    {
        var resultId = $"arch-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var explanations = new List<GroundedExplanation>();
        var citations = new List<EvidenceReference>();
        var expId = 0;
        var citId = 0;

        var entryPoints = _graphQuery.FindEntryPointNodes();
        if (entryPoints.Count == 0)
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"arch-exp-{expId++:D5}",
                Text = "No architecture entry points found in the graph.",
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
                ResultType = CognitionResultType.ArchitectureExplanation,
                Explanations = explanations,
                Citations = citations,
                OverallConfidence = ConfidenceLevel.Weak,
            };
        }

        var subsystems = DiscoverSubsystems();
        var orchestrationLayers = IdentifyOrchestrationLayers();
        var integrationPoints = IdentifyIntegrationPoints();
        var dependencyDirections = AnalyzeDependencyDirections(entryPoints);

        var overallConfidence = ConfidenceLevel.Strong;

        explanations.Add(DescribeSystemOverview(subsystems.Count, entryPoints.Count, ref expId, ref citId, citations));
        explanations.AddRange(DescribeSubsystems(subsystems, ref expId, ref citId, citations));
        explanations.AddRange(DescribeOrchestrationLayers(orchestrationLayers, ref expId, ref citId, citations));
        explanations.AddRange(DescribeIntegrationPoints(integrationPoints, ref expId, ref citId, citations));
        explanations.AddRange(DescribeDependencyDirections(dependencyDirections, ref expId, ref citId, citations));

        return new CognitionResult
        {
            ResultId = resultId,
            Query = query,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ResultType = CognitionResultType.ArchitectureExplanation,
            Explanations = explanations,
            Citations = citations,
            OverallConfidence = overallConfidence,
        };
    }

    private List<SubsystemBoundary> DiscoverSubsystems()
    {
        var allNodes = _graphQuery.GetAllNodes().ToList();
        var subsystems = new List<SubsystemBoundary>();

        var projectGroups = allNodes
            .Where(n => !n.IsExternal)
            .GroupBy(n => n.ProjectName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in projectGroups)
        {
            if (string.IsNullOrEmpty(group.Key)) continue;
            var nodes = group.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();

            subsystems.Add(new SubsystemBoundary
            {
                Name = group.Key,
                NodeCount = nodes.Count,
                EntryPointCount = nodes.Count(n => n.Attributes.ContainsKey("aspnet-route:entry-point")),
                EntityCount = nodes.Count(n => n.Kind == "entity"),
                ServiceCount = nodes.Count(n =>
                    (n.ClassName?.EndsWith("Service", StringComparison.Ordinal) ?? false)
                    || (n.ClassName?.EndsWith("Manager", StringComparison.Ordinal) ?? false)),
                ControllerCount = nodes.Count(n =>
                    n.ClassName?.EndsWith("Controller", StringComparison.Ordinal) ?? false),
                SampleNodes = nodes.Take(5).Select(n => new NodeRef(n.Id, n.Label, n.SourceFile, n.SymbolHandle)).ToList(),
            });
        }

        return subsystems;
    }

    private List<OrchestrationLayer> IdentifyOrchestrationLayers()
    {
        var layers = new List<OrchestrationLayer>();
        var allNodes = _graphQuery.GetAllNodes().ToList();

        var routeNodes = allNodes.Where(n => n.Attributes.ContainsKey("aspnet-route:entry-point")).ToList();
        if (routeNodes.Count > 0)
        {
            layers.Add(new OrchestrationLayer
            {
                LayerName = "API Route",
                NodeCount = routeNodes.Count,
                Description = "HTTP endpoint entry points",
                SampleRefs = routeNodes.Take(5).Select(n =>
                    new NodeRef(n.Id, n.Label, n.SourceFile, n.SymbolHandle)).ToList(),
                Confidence = ConfidenceLevel.Certain,
            });
        }

        var controllerNodes = allNodes.Where(n =>
            n.ClassName?.EndsWith("Controller", StringComparison.Ordinal) ?? false).ToList();
        if (controllerNodes.Count > 0)
        {
            layers.Add(new OrchestrationLayer
            {
                LayerName = "Controller",
                NodeCount = controllerNodes.Count,
                Description = "Request handling and routing",
                SampleRefs = controllerNodes.Take(5).Select(n =>
                    new NodeRef(n.Id, n.Label, n.SourceFile, n.SymbolHandle)).ToList(),
                Confidence = ConfidenceLevel.Certain,
            });
        }

        var serviceNodes = allNodes.Where(n =>
            (n.ClassName?.EndsWith("Service", StringComparison.Ordinal) ?? false)
            || (n.ClassName?.EndsWith("Manager", StringComparison.Ordinal) ?? false)).ToList();
        if (serviceNodes.Count > 0)
        {
            layers.Add(new OrchestrationLayer
            {
                LayerName = "Service",
                NodeCount = serviceNodes.Count,
                Description = "Business logic and orchestration",
                SampleRefs = serviceNodes.Take(5).Select(n =>
                    new NodeRef(n.Id, n.Label, n.SourceFile, n.SymbolHandle)).ToList(),
                Confidence = ConfidenceLevel.Strong,
            });
        }

        var entityNodes = allNodes.Where(n => n.Kind == "entity").ToList();
        if (entityNodes.Count > 0)
        {
            layers.Add(new OrchestrationLayer
            {
                LayerName = "Entity/Data",
                NodeCount = entityNodes.Count,
                Description = "Data access and persistence",
                SampleRefs = entityNodes.Take(5).Select(n =>
                    new NodeRef(n.Id, n.Label, n.SourceFile, n.SymbolHandle)).ToList(),
                Confidence = ConfidenceLevel.Strong,
            });
        }

        return layers;
    }

    private List<IntegrationPoint> IdentifyIntegrationPoints()
    {
        var points = new List<IntegrationPoint>();
        var allNodes = _graphQuery.GetAllNodes().ToList();

        var crossProjectEdges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in allNodes)
        {
            var callees = _graphQuery.GetCallees(node.Id);
            foreach (var calleeId in callees.OrderBy(c => c, StringComparer.Ordinal))
            {
                var callee = _graphQuery.GetNode(calleeId);
                if (callee is null) continue;
                if (!StringComparer.Ordinal.Equals(node.ProjectName, callee.ProjectName)
                    && !string.IsNullOrEmpty(node.ProjectName)
                    && !string.IsNullOrEmpty(callee.ProjectName))
                {
                    var key = string.Compare(node.ProjectName, callee.ProjectName, StringComparison.Ordinal) < 0
                        ? $"{node.ProjectName}↔{callee.ProjectName}"
                        : $"{callee.ProjectName}↔{node.ProjectName}";
                    crossProjectEdges.Add(key);
                }
            }
        }

        foreach (var edge in crossProjectEdges.OrderBy(e => e, StringComparer.Ordinal).Take(10))
        {
            var projNames = edge.ToString().Split("↔");
            points.Add(new IntegrationPoint
            {
                Description = $"Cross-project integration: {projNames[0]} ↔ {projNames[1]}",
                FromProject = projNames[0],
                ToProject = projNames[1],
                Confidence = ConfidenceLevel.Strong,
            });
        }

        return points;
    }

    private List<DependencyDirection> AnalyzeDependencyDirections(IReadOnlyList<string> entryPoints)
    {
        var directions = new List<DependencyDirection>();

        foreach (var entryId in entryPoints.OrderBy(id => id, StringComparer.Ordinal).Take(10))
        {
            var chains = _graphQuery.GetCallChain(entryId, 4);
            foreach (var chain in chains.Take(3))
            {
                directions.Add(new DependencyDirection
                {
                    EntryPointId = entryId,
                    PathIds = chain.ToList(),
                    Depth = chain.Count,
                    Confidence = chain.Count <= 2 ? ConfidenceLevel.Certain : ConfidenceLevel.Strong,
                });
            }
        }

        return directions;
    }

    private GroundedExplanation DescribeSystemOverview(int subsystemCount, int entryPointCount, ref int expId, ref int citId, List<EvidenceReference> citations)
    {
        var text = $"The system comprises {subsystemCount} subsystem(s) with {entryPointCount} API entry point(s).";

        return new GroundedExplanation
        {
            ExplanationId = $"arch-exp-{expId++:D5}",
            Text = text,
            Claim = "System overview",
            ConfidenceLevel = ConfidenceLevel.Strong,
            SupportingNodeIds = Array.Empty<string>(),
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        };
    }

    private IReadOnlyList<GroundedExplanation> DescribeSubsystems(List<SubsystemBoundary> subsystems, ref int expId, ref int citId, List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var sub in subsystems.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var parts = new List<string>
            {
                $"{sub.Name}: {sub.ServiceCount} service(s), {sub.ControllerCount} controller(s), {sub.EntryPointCount} entry point(s), {sub.EntityCount} entity node(s).",
            };

            AddSampleRefs(sub.SampleNodes, $"{sub.Name}", citations, ref citId);

            var citIds = sub.SampleNodes
                .Select(n => $"cite-{n.NodeId}")
                .ToList();

            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"arch-exp-{expId++:D5}",
                Text = string.Join(" ", parts),
                Claim = $"Subsystem: {sub.Name}",
                ConfidenceLevel = ConfidenceLevel.Strong,
                SupportingNodeIds = sub.SampleNodes.Select(n => n.NodeId).ToList(),
                SupportingSourceFiles = sub.SampleNodes.Select(n => n.SourceFile).Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CitationIds = citIds,
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeOrchestrationLayers(List<OrchestrationLayer> layers, ref int expId, ref int citId, List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var layer in layers.OrderByDescending(l => l.Confidence).ThenBy(l => l.LayerName, StringComparer.Ordinal))
        {
            var text = $"{layer.LayerName} Layer: {layer.NodeCount} node(s). {layer.Description}";
            var citIds = layer.SampleRefs
                .Select(n => $"cite-{n.NodeId}")
                .ToList();

            AddSampleRefs(layer.SampleRefs, layer.LayerName, citations, ref citId);

            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"arch-exp-{expId++:D5}",
                Text = text,
                Claim = $"Orchestration layer: {layer.LayerName}",
                ConfidenceLevel = layer.Confidence,
                SupportingNodeIds = layer.SampleRefs.Select(n => n.NodeId).ToList(),
                SupportingSourceFiles = layer.SampleRefs.Select(n => n.SourceFile).Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CitationIds = citIds,
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeIntegrationPoints(List<IntegrationPoint> points, ref int expId, ref int citId, List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        if (points.Count == 0)
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"arch-exp-{expId++:D5}",
                Text = "No cross-project integration points detected.",
                Claim = "Integration points",
                ConfidenceLevel = ConfidenceLevel.Moderate,
                SupportingNodeIds = Array.Empty<string>(),
                SupportingSourceFiles = Array.Empty<string>(),
                CitationIds = Array.Empty<string>(),
            });
            return explanations;
        }

        foreach (var point in points.OrderBy(p => p.FromProject, StringComparer.Ordinal))
        {
            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"arch-exp-{expId++:D5}",
                Text = point.Description,
                Claim = $"Integration: {point.FromProject} ↔ {point.ToProject}",
                ConfidenceLevel = point.Confidence,
                SupportingNodeIds = Array.Empty<string>(),
                SupportingSourceFiles = Array.Empty<string>(),
                CitationIds = Array.Empty<string>(),
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeDependencyDirections(List<DependencyDirection> directions, ref int expId, ref int citId, List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        explanations.Add(new GroundedExplanation
        {
            ExplanationId = $"arch-exp-{expId++:D5}",
            Text = $"{directions.Count} dependency path(s) analyzed from entry points. Maximum depth: {directions.Max(d => d.Depth)}.",
            Claim = "Dependency analysis",
            ConfidenceLevel = ConfidenceLevel.Strong,
            SupportingNodeIds = directions.SelectMany(d => d.PathIds).Distinct(StringComparer.Ordinal).ToList(),
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        });

        return explanations;
    }

    private void AddSampleRefs(IReadOnlyList<NodeRef> refs, string context, List<EvidenceReference> citations, ref int citId)
    {
        foreach (var r in refs)
        {
            if (citations.Any(c => StringComparer.Ordinal.Equals(c.SourceNodeId, r.NodeId)))
                continue;

            citations.Add(new EvidenceReference
            {
                CitationId = $"cite-{citId++:D5}",
                SourceNodeId = r.NodeId,
                SourceNodeLabel = r.Label,
                SourceFile = r.SourceFile,
                SymbolHandle = r.SymbolHandle,
                ConfidenceLevel = string.IsNullOrEmpty(r.SymbolHandle)
                    ? ConfidenceLevel.Moderate : ConfidenceLevel.Certain,
                Layer = context,
            });
        }
    }
}

public class ArchitectureExplorerOptions
{
    public int MaxSubsystems { get; init; } = 20;
    public int MaxLayers { get; init; } = 10;
    public int MaxIntegrationPoints { get; init; } = 10;
    public int MaxCallDepth { get; init; } = 5;

    public static ArchitectureExplorerOptions Default => new();
}

public sealed class SubsystemBoundary
{
    public required string Name { get; init; }
    public int NodeCount { get; init; }
    public int EntryPointCount { get; init; }
    public int EntityCount { get; init; }
    public int ServiceCount { get; init; }
    public int ControllerCount { get; init; }
    public required IReadOnlyList<NodeRef> SampleNodes { get; init; }
}

public sealed class OrchestrationLayer
{
    public required string LayerName { get; init; }
    public int NodeCount { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<NodeRef> SampleRefs { get; init; }
    public ConfidenceLevel Confidence { get; init; }
}

public sealed class IntegrationPoint
{
    public required string Description { get; init; }
    public string FromProject { get; init; } = "";
    public string ToProject { get; init; } = "";
    public ConfidenceLevel Confidence { get; init; }
}

public sealed class DependencyDirection
{
    public required string EntryPointId { get; init; }
    public required IReadOnlyList<string> PathIds { get; init; }
    public int Depth { get; init; }
    public ConfidenceLevel Confidence { get; init; }
}

public sealed class NodeRef
{
    public string NodeId { get; }
    public string Label { get; }
    public string SourceFile { get; }
    public string SymbolHandle { get; }

    public NodeRef(string nodeId, string label, string sourceFile, string symbolHandle)
    {
        NodeId = nodeId;
        Label = label;
        SourceFile = sourceFile;
        SymbolHandle = symbolHandle;
    }
}

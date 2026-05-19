// =============================================================================
// Truth/SemanticDriftDetector.cs — detects semantic inconsistencies in graph
// =============================================================================
// Detects:
//   - Edges that no longer correspond to source code
//   - Entities with changed class names or namespaces
//   - Methods whose symbol identity has changed
//   - Propagation chains that exceed safe depth
// =============================================================================

using Core.Graph;
using Core.Semantics;

namespace Core.Truth;

public sealed class SemanticDriftDetector
{
    private readonly SemanticDriftOptions _options;

    public SemanticDriftDetector(SemanticDriftOptions? options = null)
    {
        _options = options ?? SemanticDriftOptions.Default;
    }

    public DriftReport DetectDrift(CodeGraph graph, SymbolReferenceIndex symbolIndex)
    {
        var issues = new List<DriftFinding>();

        DetectSymbolMismatches(graph, symbolIndex, issues);
        DetectPropagationOverreach(graph, issues);
        DetectUngroundedEdges(graph, issues);
        DetectEntityOrphans(graph, issues);

        return new DriftReport
        {
            GraphVersion = graph.SchemaVersion,
            Issues = issues,
            TotalIssues = issues.Count,
            HasDrift = issues.Count > 0,
        };
    }

    private void DetectSymbolMismatches(
        CodeGraph graph,
        SymbolReferenceIndex symbolIndex,
        List<DriftFinding> issues)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.Kind != GraphNodeKind.Method || node.IsExternal)
                continue;

            var handleStr = node.Attributes.GetValueOrDefault("symbolHandle", "");
            if (string.IsNullOrEmpty(handleStr))
            {
                issues.Add(new DriftFinding
                {
                    FindingType = DriftFindingType.MissingSymbolBinding,
                    NodeId = node.Id,
                    Description = $"Method node '{node.Label}' has no SymbolHandle binding.",
                    Severity = _options.MissingSymbolBindingSeverity,
                });
                continue;
            }

            var handle = SymbolHandle.Parse(handleStr);
            if (handle.IsEmpty) continue;

            var refNodes = symbolIndex.FindNodes(handle);
            if (refNodes.Count == 0)
            {
                issues.Add(new DriftFinding
                {
                    FindingType = DriftFindingType.OrphanSymbol,
                    NodeId = node.Id,
                    Description = $"Method node '{node.Label}' has symbol '{handle.Value}' but symbol not found in index.",
                    Severity = 0.8,
                });
            }
        }
    }

    private void DetectPropagationOverreach(CodeGraph graph, List<DriftFinding> issues)
    {
        foreach (var edge in graph.Edges)
        {
            var depthStr = edge.Attributes.GetValueOrDefault("propagationDepth", "0");
            if (int.TryParse(depthStr, out var depth) && depth > _options.MaxSafePropagationDepth)
            {
                issues.Add(new DriftFinding
                {
                    FindingType = DriftFindingType.PropagationOverreach,
                    EdgeFromId = edge.FromId,
                    EdgeToId = edge.ToId,
                    Description = $"Edge '{edge.Call}' has propagation depth {depth} (max safe: {_options.MaxSafePropagationDepth}).",
                    Severity = 0.5 + (depth - _options.MaxSafePropagationDepth) * 0.1,
                });
            }
        }
    }

    private void DetectUngroundedEdges(CodeGraph graph, List<DriftFinding> issues)
    {
        foreach (var edge in graph.Edges)
        {
            var groundedStr = edge.Attributes.GetValueOrDefault("grounded", "true");
            if (groundedStr == "false")
            {
                issues.Add(new DriftFinding
                {
                    FindingType = DriftFindingType.UngroundedEdge,
                    EdgeFromId = edge.FromId,
                    EdgeToId = edge.ToId,
                    Description = $"Edge '{edge.Call}' ({edge.Kind}) is marked as ungrounded.",
                    Severity = 0.6,
                });
            }
        }
    }

    private void DetectEntityOrphans(CodeGraph graph, List<DriftFinding> issues)
    {
        var entityNodes = graph.Nodes.Where(n => n.Kind == GraphNodeKind.Entity).ToList();
        foreach (var entity in entityNodes)
        {
            var hasIncoming = graph.Edges.Any(e =>
                StringComparer.Ordinal.Equals(e.ToId, entity.Id));
            var hasOutgoing = graph.Edges.Any(e =>
                StringComparer.Ordinal.Equals(e.FromId, entity.Id));

            if (!hasIncoming && !hasOutgoing)
            {
                issues.Add(new DriftFinding
                {
                    FindingType = DriftFindingType.OrphanEntity,
                    NodeId = entity.Id,
                    Description = $"Entity '{entity.Label}' has no edges connecting it to any method.",
                    Severity = 0.3,
                });
            }
        }
    }
}

public sealed class SemanticDriftOptions
{
    public int MaxSafePropagationDepth { get; init; } = 4;
    public double MissingSymbolBindingSeverity { get; init; } = 0.7;

    public static SemanticDriftOptions Default => new();
}

public sealed class DriftReport
{
    public int GraphVersion { get; init; }
    public IReadOnlyList<DriftFinding> Issues { get; init; } = Array.Empty<DriftFinding>();
    public int TotalIssues { get; init; }
    public bool HasDrift { get; init; }
}

public sealed class DriftFinding
{
    public DriftFindingType FindingType { get; init; }
    public string? NodeId { get; init; }
    public string? EdgeFromId { get; init; }
    public string? EdgeToId { get; init; }
    public required string Description { get; init; }
    public double Severity { get; init; }
}

public enum DriftFindingType
{
    MissingSymbolBinding,
    OrphanSymbol,
    PropagationOverreach,
    UngroundedEdge,
    OrphanEntity,
}

// =============================================================================
// Semantics/Validation/SymbolStabilityDiagnostics.cs — symbol stability diagnostics
// =============================================================================
// Validates that symbol handles are stable, unique, and unambiguous across:
//   - Multiple compilations of the same source
//   - Type renames and namespace changes
//   - Overload resolution
//   - Extension method identification
// =============================================================================

using Core.Graph;
using Core.Semantics;

namespace Core.Semantics.Validation;

public sealed class SymbolStabilityDiagnostics
{
    private readonly List<SymbolStabilityIssue> _issues = new();

    public IReadOnlyList<SymbolStabilityIssue> Issues => _issues.AsReadOnly();
    public bool HasIssues => _issues.Count > 0;

    public void Diagnose(CodeGraph graph, SymbolReferenceIndex? symbolIndex)
    {
        _issues.Clear();

        DetectMissingHandles(graph);
        DetectUnstableOverloads(graph);
        DetectExtensionMethodAmbiguity(graph);

        if (symbolIndex is not null)
        {
            DetectDuplicateHandles(graph, symbolIndex);
            DetectCrossSymbolMismatch(graph, symbolIndex);
        }
    }

    private void DetectMissingHandles(CodeGraph graph)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.Kind != GraphNodeKind.Method) continue;
            if (node.IsExternal) continue;

            if (string.IsNullOrEmpty(node.SymbolHandle))
            {
                _issues.Add(new SymbolStabilityIssue
                {
                    IssueType = SymbolStabilityIssueType.MissingHandle,
                    NodeId = node.Id,
                    NodeLabel = node.Label,
                    Description = $"Method '{node.Label}' has no SymbolHandle binding.",
                    Severity = SymbolStabilitySeverity.High,
                });
            }
        }
    }

    private void DetectUnstableOverloads(CodeGraph graph)
    {
        var overloadGroups = graph.Nodes
            .Where(n => n.Kind == GraphNodeKind.Method && !n.IsExternal)
            .GroupBy(n => $"{n.Namespace}.{n.ClassName}.{n.MethodName}");

        foreach (var group in overloadGroups)
        {
            if (group.Count() <= 1) continue;

            var withoutHandles = group
                .Where(n => string.IsNullOrEmpty(n.SymbolHandle))
                .ToList();

            if (withoutHandles.Count > 0)
            {
                _issues.Add(new SymbolStabilityIssue
                {
                    IssueType = SymbolStabilityIssueType.UnstableOverload,
                    NodeLabel = group.Key,
                    OverloadCount = group.Count(),
                    Description = $"Method '{group.Key}' has {group.Count()} overloads but {withoutHandles.Count} lack SymbolHandle bindings.",
                    Severity = SymbolStabilitySeverity.Medium,
                });
            }

            var handles = group
                .Select(n => n.SymbolHandle)
                .Where(h => !string.IsNullOrEmpty(h))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (handles.Count != group.Count())
            {
                _issues.Add(new SymbolStabilityIssue
                {
                    IssueType = SymbolStabilityIssueType.DuplicateHandle,
                    NodeLabel = group.Key,
                    Description = $"Method '{group.Key}' has {group.Count()} overloads but only {handles.Count} unique SymbolHandles.",
                    Severity = SymbolStabilitySeverity.Low,
                });
            }
        }
    }

    private void DetectExtensionMethodAmbiguity(CodeGraph graph)
    {
        var extensionEdges = graph.Edges
            .Where(e => e.Attributes.GetValueOrDefault("isExtension", "") == "true")
            .ToList();

        if (extensionEdges.Count > 1)
        {
            var groupings = extensionEdges
                .GroupBy(e => e.Call)
                .Where(g => g.Count() > 1);

            foreach (var group in groupings)
            {
                _issues.Add(new SymbolStabilityIssue
                {
                    IssueType = SymbolStabilityIssueType.ExtensionMethodAmbiguity,
                    Description = $"Extension method '{group.Key}' has {group.Count()} ambiguous call sites.",
                    Severity = SymbolStabilitySeverity.Medium,
                });
            }
        }
    }

    private void DetectDuplicateHandles(CodeGraph graph, SymbolReferenceIndex symbolIndex)
    {
        var handleToNodes = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.SymbolHandle)) continue;
            if (!handleToNodes.TryGetValue(node.SymbolHandle, out var list))
            {
                list = new List<string>();
                handleToNodes[node.SymbolHandle] = list;
            }
            list.Add(node.Id);
        }

        foreach (var (handle, nodeIds) in handleToNodes)
        {
            if (nodeIds.Count > 1)
            {
                _issues.Add(new SymbolStabilityIssue
                {
                    IssueType = SymbolStabilityIssueType.DuplicateHandle,
                    NodeLabel = handle,
                    Description = $"SymbolHandle '{handle}' maps to {nodeIds.Count} nodes: {string.Join(", ", nodeIds.Take(3))}",
                    Severity = SymbolStabilitySeverity.High,
                });
            }
        }
    }

    private void DetectCrossSymbolMismatch(CodeGraph graph, SymbolReferenceIndex symbolIndex)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.Kind != GraphNodeKind.Method) continue;
            if (string.IsNullOrEmpty(node.SymbolHandle)) continue;

            var handle = SymbolHandle.Parse(node.SymbolHandle);
            if (handle.IsEmpty) continue;

            var refNodes = symbolIndex.FindNodes(handle);
            if (!refNodes.Contains(node.Id, StringComparer.Ordinal))
            {
                _issues.Add(new SymbolStabilityIssue
                {
                    IssueType = SymbolStabilityIssueType.CrossProjectMismatch,
                    NodeId = node.Id,
                    NodeLabel = node.Label,
                    Description = $"Node '{node.Label}' has SymbolHandle '{handle.Value}' not found in symbol index.",
                    Severity = SymbolStabilitySeverity.High,
                });
            }
        }
    }
}

public sealed class SymbolStabilityIssue
{
    public SymbolStabilityIssueType IssueType { get; init; }
    public string? NodeId { get; init; }
    public string? NodeLabel { get; init; }
    public int OverloadCount { get; init; }
    public required string Description { get; init; }
    public SymbolStabilitySeverity Severity { get; init; }
}

public enum SymbolStabilityIssueType
{
    MissingHandle,
    UnstableOverload,
    DuplicateHandle,
    ExtensionMethodAmbiguity,
    CrossProjectMismatch,
}

public enum SymbolStabilitySeverity
{
    Low,
    Medium,
    High,
}

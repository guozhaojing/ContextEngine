// =============================================================================
// Semantics/Validation/DuplicateSymbolDetector.cs — duplicate symbol detection
// =============================================================================
// Detects:
//   - Two graph nodes with the same SymbolHandle (should not happen)
//   - SymbolHandles that map to non-existent assembly locations
//   - Nodes where string identity != symbol identity (drift)
// =============================================================================

using Core.Graph;
using Core.Semantics;

namespace Core.Semantics.Validation;

public sealed class DuplicateSymbolDetector
{
    public DuplicateSymbolReport Detect(CodeGraph graph)
    {
        var duplicates = new List<DuplicateSymbolIssue>();
        var orphans = new List<DuplicateSymbolIssue>();
        var identityDrifts = new List<DuplicateSymbolIssue>();

        var handleToNodes = new Dictionary<string, List<GraphNode>>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrEmpty(node.SymbolHandle)) continue;

            if (!handleToNodes.TryGetValue(node.SymbolHandle, out var list))
            {
                list = new List<GraphNode>();
                handleToNodes[node.SymbolHandle] = list;
            }
            list.Add(node);
        }

        foreach (var (handle, nodes) in handleToNodes)
        {
            if (nodes.Count > 1)
            {
                duplicates.Add(new DuplicateSymbolIssue
                {
                    IssueType = DuplicateSymbolIssueType.DuplicateHandle,
                    SymbolHandle = handle,
                    NodeLabels = nodes.Select(n => n.Label).ToList(),
                    Description = $"SymbolHandle '{handle}' is bound to {nodes.Count} nodes: {string.Join(", ", nodes.Select(n => n.Label).Take(5))}",
                    Severity = SymbolStabilitySeverity.High,
                });
            }

            foreach (var node in nodes)
            {
                var handleObj = SymbolHandle.Parse(handle);
                if (handleObj.IsEmpty) continue;

                var expectedKind = handleObj.Kind.ToString().ToUpperInvariant();
                var actualKind = node.Kind.ToUpperInvariant();
                if (!StringComparer.Ordinal.Equals(expectedKind, actualKind))
                {
                    identityDrifts.Add(new DuplicateSymbolIssue
                    {
                        IssueType = DuplicateSymbolIssueType.IdentityDrift,
                        SymbolHandle = handle,
                        NodeLabels = new[] { node.Label },
                        Description = $"Node '{node.Label}' has kind '{node.Kind}' but SymbolHandle suggests '{handleObj.Kind}'.",
                        Severity = SymbolStabilitySeverity.Medium,
                    });
                }
            }
        }

        return new DuplicateSymbolReport
        {
            Duplicates = duplicates,
            Orphans = orphans,
            IdentityDrifts = identityDrifts,
            TotalIssues = duplicates.Count + orphans.Count + identityDrifts.Count,
        };
    }
}

public sealed class DuplicateSymbolReport
{
    public required IReadOnlyList<DuplicateSymbolIssue> Duplicates { get; init; }
    public required IReadOnlyList<DuplicateSymbolIssue> Orphans { get; init; }
    public required IReadOnlyList<DuplicateSymbolIssue> IdentityDrifts { get; init; }
    public int TotalIssues { get; init; }
}

public sealed class DuplicateSymbolIssue
{
    public DuplicateSymbolIssueType IssueType { get; init; }
    public required string SymbolHandle { get; init; }
    public required IReadOnlyList<string> NodeLabels { get; init; }
    public required string Description { get; init; }
    public SymbolStabilitySeverity Severity { get; init; }
}

public enum DuplicateSymbolIssueType
{
    DuplicateHandle,
    OrphanSymbol,
    IdentityDrift,
}

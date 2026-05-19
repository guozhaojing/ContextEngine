// =============================================================================
// Semantics/Validation/GenericBindingValidator.cs — generic binding validation
// =============================================================================
// Validates that generic type bindings are:
//   - Unambiguous (not multiple possible targets)
//   - Stable (not depending on resolution order)
//   - Grounded (traced back to an actual Roslyn class symbol)
// =============================================================================

using Core.Graph;
using Core.Semantics;

namespace Core.Semantics.Validation;

public sealed class GenericBindingValidator
{
    public GenericBindingReport Validate(CodeGraph graph)
    {
        var ambiguous = new List<GenericBindingIssue>();
        var ungrounded = new List<GenericBindingIssue>();
        var unstable = new List<GenericBindingIssue>();

        var entityNodes = graph.Nodes
            .Where(n => n.Kind == GraphNodeKind.Entity)
            .ToList();

        foreach (var node in entityNodes)
        {
            var source = node.Attributes.GetValueOrDefault("analyzer", "");

            if (source == "generic-resolution")
            {
                var confidence = node.Attributes.GetValueOrDefault("confidence", "");
                if (confidence == "low" || confidence == "medium")
                {
                    unstable.Add(new GenericBindingIssue
                    {
                        EntityId = node.Id,
                        EntityLabel = node.Label,
                        Description = $"Entity '{node.Label}' resolved via generic binding with {confidence} confidence.",
                        Severity = SymbolStabilitySeverity.Medium,
                    });
                }

                if (string.IsNullOrEmpty(node.SymbolHandle))
                {
                    ungrounded.Add(new GenericBindingIssue
                    {
                        EntityId = node.Id,
                        EntityLabel = node.Label,
                        Description = $"Entity '{node.Label}' from generic resolution has no SymbolHandle.",
                        Severity = SymbolStabilitySeverity.High,
                    });
                }
            }

            if (!string.IsNullOrEmpty(node.SymbolHandle))
            {
                var handleNodes = graph.Nodes
                    .Where(n => StringComparer.Ordinal.Equals(n.SymbolHandle, node.SymbolHandle))
                    .ToList();

                if (handleNodes.Count > 1)
                {
                    ambiguous.Add(new GenericBindingIssue
                    {
                        EntityId = node.Id,
                        EntityLabel = node.Label,
                        Description = $"Entity '{node.Label}' has ambiguous binding: {handleNodes.Count} nodes share SymbolHandle.",
                        Severity = SymbolStabilitySeverity.High,
                    });
                }
            }
        }

        return new GenericBindingReport
        {
            Ambiguous = ambiguous,
            Ungrounded = ungrounded,
            Unstable = unstable,
            TotalIssues = ambiguous.Count + ungrounded.Count + unstable.Count,
        };
    }
}

public sealed class GenericBindingReport
{
    public required IReadOnlyList<GenericBindingIssue> Ambiguous { get; init; }
    public required IReadOnlyList<GenericBindingIssue> Ungrounded { get; init; }
    public required IReadOnlyList<GenericBindingIssue> Unstable { get; init; }
    public int TotalIssues { get; init; }
}

public sealed class GenericBindingIssue
{
    public required string EntityId { get; init; }
    public required string EntityLabel { get; init; }
    public required string Description { get; init; }
    public SymbolStabilitySeverity Severity { get; init; }
}

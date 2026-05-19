// =============================================================================
// Semantics/SymbolReferenceIndex.cs — SymbolHandle → NodeId lookup
// =============================================================================
// Fast lookup from a stable symbol reference to the graph node(s) that represent it.
// This replaces string-namespace matching for entity/method resolution.
// =============================================================================

using Core.Graph;

namespace Core.Semantics;

public sealed class SymbolReferenceIndex
{
    private readonly Dictionary<string, List<string>> _byHandle = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byAssemblyQualified = new(StringComparer.Ordinal);

    public SymbolReferenceIndex(CodeGraph graph)
    {
        Build(graph);
    }

    private void Build(CodeGraph graph)
    {
        foreach (var node in graph.Nodes)
        {
            var handleStr = node.Attributes.GetValueOrDefault("symbolHandle", "");
            if (string.IsNullOrEmpty(handleStr))
                continue;

            if (SymbolHandle.TryParse(handleStr, out var handle) && !handle.IsEmpty)
            {
                if (!_byHandle.TryGetValue(handle.Value, out var list))
                {
                    list = new List<string>();
                    _byHandle[handle.Value] = list;
                }
                list.Add(node.Id);
            }
        }
    }

    public IReadOnlyList<string> FindNodes(SymbolHandle handle)
    {
        if (handle.IsEmpty) return Array.Empty<string>();
        return _byHandle.TryGetValue(handle.Value, out var list)
            ? list.AsReadOnly()
            : Array.Empty<string>();
    }

    public string? FindFirstNode(SymbolHandle handle)
    {
        var nodes = FindNodes(handle);
        return nodes.Count > 0 ? nodes[0] : null;
    }

    public bool Contains(SymbolHandle handle)
    {
        return !handle.IsEmpty && _byHandle.ContainsKey(handle.Value);
    }

    public int Count => _byHandle.Count;
}

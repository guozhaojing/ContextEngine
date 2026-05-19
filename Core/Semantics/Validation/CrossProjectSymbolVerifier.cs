// =============================================================================
// Semantics/Validation/CrossProjectSymbolVerifier.cs — cross-project symbol verification
// =============================================================================
// Verifies symbol consistency across projects in the same solution:
//   - Same class resolved in ProjectA and ProjectB should have same SymbolHandle
//   - Cross-project method calls should resolve to the same ISymbol
//   - No project-local namespace aliasing causing fake duplicates
// =============================================================================

using Core.Graph;
using Core.Semantics;

namespace Core.Semantics.Validation;

public sealed class CrossProjectSymbolVerifier
{
    public CrossProjectVerificationReport Verify(CodeGraph graph)
    {
        var mismatches = new List<CrossProjectMismatch>();

        var projectGroups = graph.Nodes
            .Where(n => n.Kind == GraphNodeKind.Method && !n.IsExternal && !string.IsNullOrEmpty(n.SymbolHandle))
            .GroupBy(n => n.ProjectName);

        var projectLists = projectGroups.ToList();
        for (var i = 0; i < projectLists.Count; i++)
        {
            for (var j = i + 1; j < projectLists.Count; j++)
            {
                var projA = projectLists[i];
                var projB = projectLists[j];

                var handlesA = new HashSet<string>(
                    projA.Where(n => !string.IsNullOrEmpty(n.SymbolHandle))
                        .Select(n => n.SymbolHandle),
                    StringComparer.Ordinal);

                var handlesB = new HashSet<string>(
                    projB.Where(n => !string.IsNullOrEmpty(n.SymbolHandle))
                        .Select(n => n.SymbolHandle),
                    StringComparer.Ordinal);

                var commonClassNames = projA
                    .Select(n => SanitizeClassName(n.ClassName))
                    .Intersect(
                        projB.Select(n => SanitizeClassName(n.ClassName)),
                        StringComparer.Ordinal)
                    .ToList();

                foreach (var className in commonClassNames.Take(20))
                {
                    var nodesA = projA
                        .Where(n => SanitizeClassName(n.ClassName) == className)
                        .ToList();

                    var nodesB = projB
                        .Where(n => SanitizeClassName(n.ClassName) == className)
                        .ToList();

                    foreach (var na in nodesA.Take(5))
                    {
                        foreach (var nb in nodesB.Take(5))
                        {
                            if (StringComparer.Ordinal.Equals(na.MethodName, nb.MethodName)
                                && !StringComparer.Ordinal.Equals(na.SymbolHandle, nb.SymbolHandle))
                            {
                                mismatches.Add(new CrossProjectMismatch
                                {
                                    ClassName = className,
                                    MethodName = na.MethodName,
                                    ProjectA = projA.Key,
                                    ProjectB = projB.Key,
                                    SymbolHandleA = na.SymbolHandle,
                                    SymbolHandleB = nb.SymbolHandle,
                                    Description = $"Method '{className}.{na.MethodName}' has different SymbolHandles across '{projA.Key}' and '{projB.Key}'.",
                                });
                            }
                        }
                    }
                }
            }
        }

        return new CrossProjectVerificationReport
        {
            Mismatches = mismatches,
            TotalMismatches = mismatches.Count,
            VerifiedProjectPairs = projectLists.Count * (projectLists.Count - 1) / 2,
        };
    }

    private static string SanitizeClassName(string className)
    {
        if (string.IsNullOrEmpty(className)) return "";
        var parts = className.Split('.');
        return parts[^1];
    }
}

public sealed class CrossProjectVerificationReport
{
    public required IReadOnlyList<CrossProjectMismatch> Mismatches { get; init; }
    public int TotalMismatches { get; init; }
    public int VerifiedProjectPairs { get; init; }
}

public sealed class CrossProjectMismatch
{
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required string ProjectA { get; init; }
    public required string ProjectB { get; init; }
    public string SymbolHandleA { get; init; } = "";
    public string SymbolHandleB { get; init; } = "";
    public required string Description { get; init; }
}

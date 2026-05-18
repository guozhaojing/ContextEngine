using Microsoft.CodeAnalysis;

namespace Core.Semantics;

internal static class DefaultMetadataReferenceProvider
{
    private static readonly object Sync = new();
    private static IReadOnlyList<MetadataReference>? _references;

    public static IReadOnlyList<MetadataReference> GetReferences()
    {
        if (_references is not null)
            return _references;

        lock (Sync)
        {
            _references ??= CreateReferences();
            return _references;
        }
    }

    private static IReadOnlyList<MetadataReference> CreateReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly
        };

        return assemblies
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToList();
    }
}

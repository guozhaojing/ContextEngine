// =============================================================================
// Semantics/DefaultMetadataReferenceProvider.cs
// =============================================================================
// 当 MSBuild 无法加载项目时，用最小引用集创建单文件 SemanticModel 的回退方案。
// =============================================================================

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

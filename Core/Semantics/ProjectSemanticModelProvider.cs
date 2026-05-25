// =============================================================================
// Semantics/ProjectSemanticModelProvider.cs — per-project compilation (lazy build)
// =============================================================================
// v3: Accumulates trees per project; compiles ONCE per project when all files done.
//     Scanner calls FinalizeProject() after processing all files in a project.
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Core.Semantics;

public sealed class ProjectSemanticModelProvider : IAsyncDisposable
{
    private readonly Dictionary<string, CompilationCache> _caches = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private static CSharpCompilationOptions CompilationOptions =>
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

    public void AddSyntaxTree(string projectFilePath, SyntaxTree syntaxTree)
    {
        lock (_lock)
        {
            if (!_caches.TryGetValue(projectFilePath, out var cc))
            {
                cc = new CompilationCache();
                _caches[projectFilePath] = cc;
            }
            cc.Trees.Add(syntaxTree);
        }
    }

    public void FinalizeProject(string projectFilePath)
    {
        lock (_lock)
        {
            if (_caches.TryGetValue(projectFilePath, out var cc) && cc.Trees.Count > 0)
            {
                cc.Compilation = CSharpCompilation.Create(
                    $"ctx_{projectFilePath.GetHashCode():X8}",
                    cc.Trees,
                    DefaultMetadataReferenceProvider.GetReferences(),
                    CompilationOptions);
            }
        }
    }

    public Task<SemanticModel> GetSemanticModelAsync(
        string projectFilePath,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        AddSyntaxTree(projectFilePath, syntaxTree);

        Compilation? compilation = null;
        lock (_lock)
        {
            if (_caches.TryGetValue(projectFilePath, out var cc))
                compilation = cc.Compilation;
        }

        // If project not finalized yet, use standalone compilation (fast, per-file)
        if (compilation is null)
        {
            compilation = CSharpCompilation.Create(
                $"fallback_{Guid.NewGuid():N}",
                [syntaxTree],
                DefaultMetadataReferenceProvider.GetReferences(),
                CompilationOptions);
        }

        return Task.FromResult(compilation.GetSemanticModel(syntaxTree));
    }

    /// <summary>
    /// Get a SemanticModel from the finalized project compilation.
    /// Call only after <see cref="FinalizeProject"/>.
    /// </summary>
    public SemanticModel GetSemanticModel(string projectFilePath, SyntaxTree syntaxTree)
    {
        lock (_lock)
        {
            if (_caches.TryGetValue(projectFilePath, out var cc) && cc.Compilation is not null)
                return cc.Compilation.GetSemanticModel(syntaxTree);
        }
        throw new InvalidOperationException(
            $"Project not finalized: {projectFilePath}. Call FinalizeProject() first.");
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock) { _caches.Clear(); }
        return ValueTask.CompletedTask;
    }

    private sealed class CompilationCache
    {
        public List<SyntaxTree> Trees { get; } = new();
        public CSharpCompilation? Compilation { get; set; }
    }
}

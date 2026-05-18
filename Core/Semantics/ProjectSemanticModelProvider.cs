using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace Core.Semantics;

public sealed class ProjectSemanticModelProvider : IAsyncDisposable
{
    private static int _msbuildRegistered;
    private readonly MSBuildWorkspace _workspace = MSBuildWorkspace.Create();
    private readonly Dictionary<string, Project> _projectCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SemanticModel> GetSemanticModelAsync(
        string projectFilePath,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(syntaxTree.FilePath);
        var semanticModel = await TryGetFromMsBuildAsync(projectFilePath, sourcePath, cancellationToken)
            .ConfigureAwait(false);

        return semanticModel ?? CreateFallbackSemanticModel(syntaxTree);
    }

    private async Task<SemanticModel?> TryGetFromMsBuildAsync(
        string projectFilePath,
        string sourceFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureMsBuildRegistered();
            var project = await GetOrOpenProjectAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
            var document = project.Documents.FirstOrDefault(doc =>
                doc.FilePath is not null
                && string.Equals(Path.GetFullPath(doc.FilePath), sourceFilePath, StringComparison.OrdinalIgnoreCase));

            if (document is null)
                return null;

            return await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Project> GetOrOpenProjectAsync(
        string projectFilePath,
        CancellationToken cancellationToken)
    {
        var fullProjectPath = Path.GetFullPath(projectFilePath);
        if (_projectCache.TryGetValue(fullProjectPath, out var cached))
            return cached;

        var project = await _workspace.OpenProjectAsync(fullProjectPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _projectCache[fullProjectPath] = project;
        return project;
    }

    private static SemanticModel CreateFallbackSemanticModel(SyntaxTree syntaxTree)
    {
        var compilation = CSharpCompilation.Create(
            $"ContextEngine_Fallback_{Guid.NewGuid():N}",
            [syntaxTree],
            DefaultMetadataReferenceProvider.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetSemanticModel(syntaxTree);
    }

    private static void EnsureMsBuildRegistered()
    {
        if (Interlocked.Exchange(ref _msbuildRegistered, 1) == 1)
            return;

        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();
    }

    public ValueTask DisposeAsync()
    {
        _workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

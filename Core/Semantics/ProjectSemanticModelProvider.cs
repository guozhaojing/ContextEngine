using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Core.Semantics;

public sealed class ProjectSemanticModelProvider : IAsyncDisposable
{
    public Task<SemanticModel> GetSemanticModelAsync(
        string projectFilePath,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var compilation = CSharpCompilation.Create(
            $"ContextEngine_Fallback_{Guid.NewGuid():N}",
            [syntaxTree],
            DefaultMetadataReferenceProvider.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return Task.FromResult(compilation.GetSemanticModel(syntaxTree));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

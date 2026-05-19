// =============================================================================
// Semantics/SymbolGraphBuilder.cs — builds symbol-grounded graph nodes
// =============================================================================
// Extends/refines the existing CodeGraphBuilder to:
//   1. Bind EVERY method node to its ISymbol via SymbolHandle
//   2. Bind EVERY type/class identified by analyzers to a SymbolHandle
//   3. Store symbol handle in node.Attributes["symbolHandle"]
//   4. Store source file in node.Attributes["sourceFile"]
//   5. Mark grounding kind in node.Attributes["groundingKind"]
//
// This is NOT a separate graph — it enriches the existing CodeGraph.
// =============================================================================

using System.Collections.Concurrent;
using Core.Graph;
using Core.Graph.Identity;
using Core.Models;
using Core.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Semantics;

public sealed class SymbolGraphBuilder
{
    private readonly ConcurrentDictionary<string, SymbolHandle> _resolvedHandles = new(StringComparer.Ordinal);

    public SymbolReferenceIndex EnrichGraph(CodeGraph graph, SolutionScanResult scan)
    {
        var filePaths = scan.AllCodeUnits
            .Select(u => u.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var filePath in filePaths)
        {
            EnrichFile(graph, scan, filePath);
        }

        return new SymbolReferenceIndex(graph);
    }

    private void EnrichFile(CodeGraph graph, SolutionScanResult scan, string filePath)
    {
        var units = scan.AllCodeUnits
            .Where(u => StringComparer.OrdinalIgnoreCase.Equals(u.FilePath, filePath))
            .ToList();

        if (units.Count == 0) return;

        var sourceText = units[0].Content;
        if (string.IsNullOrEmpty(sourceText)) return;

        var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetRoot();

        dynamic? compilation = null;
        Microsoft.CodeAnalysis.SemanticModel? semanticModel = null;

        foreach (var unit in units)
        {
            var nodeId = MethodIdBuilder.FromCodeUnit(unit).Value;
            var node = graph.Nodes.FirstOrDefault(n =>
                StringComparer.Ordinal.Equals(n.Id, nodeId));

            if (node is null)
                continue;

            if (semanticModel is null)
            {
                compilation = CreateCompilation(syntaxTree, scan);
                semanticModel = compilation!.GetSemanticModel(syntaxTree);
            }

            var methodDecl = FindMethodDeclaration(root, unit);
            if (methodDecl is not null && semanticModel is not null)
            {
                var handle = SemanticSymbolResolver.ResolveDeclaredMethodHandle(
                    methodDecl, semanticModel);

                if (!handle.IsEmpty)
                {
                    node.Attributes["symbolHandle"] = handle.Value;
                    node.Attributes["groundingKind"] = "semantic-method";
                    _resolvedHandles[node.Id] = handle;
                }
            }

            node.Attributes["sourceFile"] = filePath;
        }

        EnrichTypeDeclarations(graph, root, semanticModel, filePath);
    }

    private void EnrichTypeDeclarations(
        CodeGraph graph,
        SyntaxNode root,
        Microsoft.CodeAnalysis.SemanticModel? semanticModel,
        string filePath)
    {
        if (semanticModel is null) return;

        var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var classDecl in classDecls)
        {
            var handle = SemanticSymbolResolver.ResolveTypeHandle(classDecl, semanticModel);
            if (handle.IsEmpty) continue;

            RegenerateBindEntityNodes(graph, handle, filePath);
        }
    }

    private void RegenerateBindEntityNodes(
        CodeGraph graph,
        SymbolHandle typeHandle,
        string sourceFile)
    {
        foreach (var node in graph.Nodes.Where(n =>
            n.Kind == GraphNodeKind.Entity
            && StringComparer.Ordinal.Equals(n.Attributes.GetValueOrDefault("sourceFile", ""), sourceFile)))
        {
            node.Attributes["symbolHandle"] = typeHandle.Value;
            node.Attributes["groundingKind"] = "semantic-type";
        }
    }

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation CreateCompilation(
        SyntaxTree syntaxTree,
        SolutionScanResult scan)
    {
        var project = scan.Projects.FirstOrDefault();
        var refs = new List<MetadataReference>();
        refs.AddRange(DefaultMetadataReferenceProvider.GetReferences());

        return Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "SymbolGraph",
            new[] { syntaxTree },
            refs,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
    }

    private static MethodDeclarationSyntax? FindMethodDeclaration(
        SyntaxNode root,
        CodeUnit unit)
    {
        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (StringComparer.Ordinal.Equals(methodDecl.Identifier.Text, unit.MethodName))
            {
                var paramTypes = methodDecl.ParameterList.Parameters
                    .Select(p => p.Type?.ToString() ?? "?")
                    .ToList();

                if (paramTypes.SequenceEqual(unit.ParameterTypes, StringComparer.Ordinal))
                    return methodDecl;
            }
        }

        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
                StringComparer.Ordinal.Equals(m.Identifier.Text, unit.MethodName));
    }
}

using Core.Graph.Identity;
using Core.Models;
using Core.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Scanning;

public class ProjectCodeScanner
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    public async Task<SolutionScanResult> ScanAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var projects = SolutionProjectDiscovery.Discover(path);
        var scanRoot = ResolveScanRoot(path);

        var result = new SolutionScanResult { ScanRoot = scanRoot };

        if (projects.Count == 0)
            throw new InvalidOperationException($"No .csproj found under: {scanRoot}");

        await using var semanticProvider = new ProjectSemanticModelProvider();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var group = new ProjectScanGroup
            {
                ProjectName = project.Name,
                ProjectPath = ToRelativePath(scanRoot, project.ProjectFilePath)
            };

            foreach (var filePath in EnumerateCsFiles(project.ProjectDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourceText = await File.ReadAllTextAsync(filePath, cancellationToken);
                var tree = CSharpSyntaxTree.ParseText(
                    sourceText,
                    path: filePath,
                    encoding: System.Text.Encoding.UTF8,
                    cancellationToken: cancellationToken);

                var semanticModel = await semanticProvider
                    .GetSemanticModelAsync(project.ProjectFilePath, tree, cancellationToken)
                    .ConfigureAwait(false);

                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                group.CodeUnits.AddRange(ExtractCodeUnits(
                    root,
                    filePath,
                    project,
                    scanRoot,
                    semanticModel));
            }

            result.Projects.Add(group);
        }

        return result;
    }

    private static string ResolveScanRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            return Path.GetDirectoryName(fullPath) ?? fullPath;

        return fullPath;
    }

    private static string ToRelativePath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static IEnumerable<string> EnumerateCsFiles(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderExcludedDirectory(path, projectDirectory))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static bool IsUnderExcludedDirectory(string filePath, string projectDirectory)
    {
        var relative = Path.GetRelativePath(projectDirectory, filePath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (ExcludedDirectoryNames.Contains(segment))
                return true;
        }

        return false;
    }

    private static IEnumerable<CodeUnit> ExtractCodeUnits(
        SyntaxNode root,
        string filePath,
        DiscoveredProject project,
        string scanRoot,
        SemanticModel semanticModel)
    {
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var namespaceName = GetNamespace(classDecl);
            var className = GetTypeName(classDecl);

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var resolvedCalls = ExtractResolvedInvocations(method, semanticModel);

                yield return new CodeUnit
                {
                    Id = MethodIdBuilder.FromMethod(
                        ToRelativePath(scanRoot, project.ProjectFilePath),
                        namespaceName,
                        className,
                        method.Identifier.Text).Value,
                    FilePath = filePath,
                    RelativeFilePath = ToRelativePath(scanRoot, filePath),
                    ProjectName = project.Name,
                    ProjectPath = ToRelativePath(scanRoot, project.ProjectFilePath),
                    Namespace = namespaceName,
                    ClassName = className,
                    MethodName = method.Identifier.Text,
                    Content = GetMethodBody(method),
                    ResolvedCalls = resolvedCalls,
                    Calls = resolvedCalls
                        .Select(ResolvedMethodInfoFormatter.ToQualifiedName)
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                };
            }
        }
    }

    private static List<ResolvedMethodInfo> ExtractResolvedInvocations(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel)
    {
        var results = new List<ResolvedMethodInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var resolved = InvocationSemanticResolver.Resolve(invocation, semanticModel);
            var key = $"{resolved.Namespace}|{resolved.ClassName}|{resolved.MethodName}|{resolved.IsExternal}";

            if (!seen.Add(key))
                continue;

            results.Add(resolved);
        }

        return results;
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var parts = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(ns => ns.Name.ToString());

        return string.Join(".", parts);
    }

    private static string GetTypeName(TypeDeclarationSyntax type)
    {
        var names = new List<string>();
        for (SyntaxNode? current = type; current is TypeDeclarationSyntax typeDecl; current = current.Parent)
            names.Insert(0, typeDecl.Identifier.Text);

        return string.Join(".", names);
    }

    private static string GetMethodBody(MethodDeclarationSyntax method)
    {
        if (method.Body is not null)
            return method.Body.ToString();

        if (method.ExpressionBody is not null)
            return method.ExpressionBody.ToString();

        return "";
    }
}

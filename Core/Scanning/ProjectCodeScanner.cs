// =============================================================================
// Scanning/ProjectCodeScanner.cs — 项目源码扫描器 (two-pass parallel)
// =============================================================================
// Pass 1: parallel parse all .cs files, add syntax trees to provider.
// Pass 2: finalize project compilation, then parallel resolve semantics.
// =============================================================================

using System.Collections.Concurrent;
using App.Infrastructure;
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

    /// <summary>
    /// Scan path (dir / .sln / .csproj), return all method CodeUnits.
    /// Two-pass per project: parse all files first, then resolve with unified compilation.
    /// </summary>
    public async Task<SolutionScanResult> ScanAsync(
        string path,
        CancellationToken cancellationToken = default,
        IProgress<LoadProgress>? progress = null)
    {
        progress?.Report(new LoadProgress { Stage = "discovering" });

        var projects = SolutionProjectDiscovery.Discover(path);
        var scanRoot = ResolveScanRoot(path);

        var result = new SolutionScanResult { ScanRoot = scanRoot };

        if (projects.Count == 0)
            throw new InvalidOperationException($"No .csproj found under: {scanRoot}");

        await using var semanticProvider = new ProjectSemanticModelProvider();
        var projectIndex = 0;

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projectIndex++;

            var projectDir = project.ProjectDirectory;
            var projectPath = project.ProjectFilePath;
            var relativeProjectPath = ToRelativePath(scanRoot, projectPath);

            var group = new ProjectScanGroup
            {
                ProjectName = project.Name,
                ProjectPath = relativeProjectPath,
            };

            // ── Pass 1: parallel parse all .cs files ──
            var csFiles = EnumerateCsFiles(projectDir).ToList();
            var totalFiles = csFiles.Count;

            progress?.Report(new LoadProgress
            {
                Stage = "parsing",
                CurrentProject = projectIndex,
                TotalProjects = projects.Count,
                CurrentFile = 0,
                TotalFiles = totalFiles,
            });

            var parseResults = new ConcurrentBag<(string FilePath, SyntaxTree Tree)>();

            await Task.Run(() =>
            {
                Parallel.ForEach(csFiles, new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                },
                filePath =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourceText = File.ReadAllText(filePath);
                    var tree = CSharpSyntaxTree.ParseText(
                        sourceText,
                        path: filePath,
                        encoding: System.Text.Encoding.UTF8,
                        cancellationToken: cancellationToken);

                    semanticProvider.AddSyntaxTree(projectPath, tree);
                    parseResults.Add((filePath, tree));

                    var current = Interlocked.Increment(ref _parseCount);
                    if (current % 10 == 0 || current == totalFiles)
                    {
                        progress?.Report(new LoadProgress
                        {
                            Stage = "parsing",
                            CurrentProject = projectIndex,
                            TotalProjects = projects.Count,
                            CurrentFile = current,
                            TotalFiles = totalFiles,
                            CurrentFilePath = Path.GetFileName(filePath),
                        });
                    }
                });
            }, cancellationToken);

            // ── Finalize: single compilation for the entire project ──
            progress?.Report(new LoadProgress
            {
                Stage = "resolving",
                CurrentProject = projectIndex,
                TotalProjects = projects.Count,
                CurrentFile = 0,
                TotalFiles = totalFiles,
            });

            semanticProvider.FinalizeProject(projectPath);

            // ── Pass 2: parallel resolve semantics using unified compilation ──
            var codeUnits = new ConcurrentBag<CodeUnit>();
            var resolveIndex = 0;
            var parseResultList = parseResults.ToList();

            await Task.Run(() =>
            {
                Parallel.ForEach(parseResultList, new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                },
                item =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var semanticModel = semanticProvider.GetSemanticModel(projectPath, item.Tree);
                    var root = item.Tree.GetRoot(cancellationToken);

                    foreach (var unit in ExtractCodeUnits(root, item.FilePath, project, scanRoot, semanticModel))
                        codeUnits.Add(unit);

                    var current = Interlocked.Increment(ref resolveIndex);
                    if (current % 10 == 0 || current == totalFiles)
                    {
                        progress?.Report(new LoadProgress
                        {
                            Stage = "resolving",
                            CurrentProject = projectIndex,
                            TotalProjects = projects.Count,
                            CurrentFile = current,
                            TotalFiles = totalFiles,
                            CurrentFilePath = Path.GetFileName(item.FilePath),
                        });
                    }
                });
            }, cancellationToken);

            group.CodeUnits = MergeDuplicateCodeUnits(codeUnits.ToList());
            result.Projects.Add(group);
        }

        progress?.Report(new LoadProgress { Stage = "complete", IsComplete = true });
        return result;
    }

    // Shared counter for progress across parallel loops (static to avoid capturing)
    private int _parseCount;

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

    /// <summary>
    /// 从一个语法树中提取所有 class 下的 method，并解析其内部调用。
    /// </summary>
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
                var paramTypes = method.ParameterList.Parameters
                    .Select(p => p.Type?.ToString() ?? "")
                    .Where(t => t.Length > 0)
                    .ToList();

                yield return new CodeUnit
                {
                    Id = MethodIdBuilder.FromMethod(
                        ToRelativePath(scanRoot, project.ProjectFilePath),
                        namespaceName,
                        className,
                        method.Identifier.Text,
                        paramTypes).Value,
                    FilePath = filePath,
                    RelativeFilePath = ToRelativePath(scanRoot, filePath),
                    ProjectName = project.Name,
                    ProjectPath = ToRelativePath(scanRoot, project.ProjectFilePath),
                    Namespace = namespaceName,
                    ClassName = className,
                    MethodName = method.Identifier.Text,
                    ParameterTypes = paramTypes,
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

    /// <summary>
    /// 遍历方法体内的 InvocationExpressionSyntax，逐个做语义解析。
    /// </summary>
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

    /// <summary>支持嵌套类：Outer.Inner</summary>
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

    private static List<CodeUnit> MergeDuplicateCodeUnits(List<CodeUnit> units)
    {
        if (units.Count <= 1)
            return units;

        var merged = new List<CodeUnit>(units.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var unit in units)
        {
            if (seen.TryGetValue(unit.Id, out var index))
            {
                var existing = merged[index];
                MergeInto(existing, unit);
            }
            else
            {
                seen[unit.Id] = merged.Count;
                merged.Add(unit);
            }
        }

        return merged;
    }

    private static void MergeInto(CodeUnit target, CodeUnit source)
    {
        if (string.IsNullOrEmpty(target.Content) && !string.IsNullOrEmpty(source.Content))
        {
            target.Content = source.Content;
            target.FilePath = source.FilePath;
            target.RelativeFilePath = source.RelativeFilePath;
        }

        foreach (var call in source.ResolvedCalls)
        {
            var key = $"{call.Namespace}|{call.ClassName}|{call.MethodName}|{call.IsExternal}";
            if (!target.ResolvedCalls.Any(c =>
                c.Namespace == call.Namespace &&
                c.ClassName == call.ClassName &&
                c.MethodName == call.MethodName &&
                c.IsExternal == call.IsExternal))
            {
                target.ResolvedCalls.Add(call);
            }
        }

        foreach (var call in source.Calls)
        {
            if (!target.Calls.Contains(call, StringComparer.Ordinal))
                target.Calls.Add(call);
        }
    }
}

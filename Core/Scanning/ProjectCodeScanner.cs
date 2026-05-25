// =============================================================================
// Scanning/ProjectCodeScanner.cs — 项目源码扫描器
// =============================================================================
// 职责：
//   1. 枚举每个项目下的 .cs 文件
//   2. 用 SyntaxTree 解析类与方法
//   3. 用 SemanticModel + InvocationSemanticResolver 解析方法内的调用
//   4. 输出 CodeUnit 列表
// 注意：此处不做图构建，不做图查询。
// =============================================================================

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
    /// 扫描指定路径（目录 / .sln / .csproj），返回所有方法的 CodeUnit。
    /// </summary>
    public async Task<SolutionScanResult> ScanAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var projects = SolutionProjectDiscovery.Discover(path);
        var scanRoot = ResolveScanRoot(path);

        var result = new SolutionScanResult { ScanRoot = scanRoot };

        if (projects.Count == 0)
            throw new InvalidOperationException($"No .csproj found under: {scanRoot}");

        // MSBuild 提供完整 SemanticModel；失败时回退到单文件编译
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

            group.CodeUnits = MergeDuplicateCodeUnits(group.CodeUnits);

            result.Projects.Add(group);

            // Finalize project compilation for cross-file resolution
            semanticProvider.FinalizeProject(project.ProjectFilePath);
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

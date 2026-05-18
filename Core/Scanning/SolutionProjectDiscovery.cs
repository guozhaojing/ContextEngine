// =============================================================================
// Scanning/SolutionProjectDiscovery.cs — 发现解决方案中的所有项目
// =============================================================================
// 支持输入：目录、.sln 文件、单个 .csproj 文件。
// 会跳过 bin/obj/.git 等目录。
// =============================================================================

using System.Text.RegularExpressions;

namespace Core.Scanning;

public static class SolutionProjectDiscovery
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    // 匹配 .sln 中的项目行：Project(...) = "Name", "path\\proj.csproj", "{guid}"
    private static readonly Regex SolutionProjectLineRegex = new(
        @"^Project\(""{[^""]+}""\)\s*=\s*""([^""]+)"",\s*""([^""]+)"",\s*""\{[^""]+\}""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 根据路径发现所有待扫描的 csproj 项目。
    /// </summary>
    public static IReadOnlyList<DiscoveredProject> Discover(string path)
    {
        if (File.Exists(path))
            return DiscoverFromFile(Path.GetFullPath(path));

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Path not found: {path}");

        return DiscoverFromDirectory(Path.GetFullPath(path));
    }

    private static IReadOnlyList<DiscoveredProject> DiscoverFromFile(string filePath)
    {
        if (filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return DeduplicateProjects(ParseSolution(filePath));

        if (filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return [ToDiscoveredProject(filePath)];

        throw new NotSupportedException($"Unsupported file type: {filePath}");
    }

    private static IReadOnlyList<DiscoveredProject> DiscoverFromDirectory(string directory)
    {
        // 优先：目录下有 .sln 则解析解决方案
        var solutionFiles = EnumerateFiles(directory, "*.sln").ToList();
        if (solutionFiles.Count > 0)
        {
            var projects = solutionFiles
                .SelectMany(ParseSolution)
                .ToList();

            return DeduplicateProjects(projects);
        }

        // 否则：递归查找所有 .csproj
        var projectFiles = EnumerateFiles(directory, "*.csproj")
            .Select(ToDiscoveredProject)
            .ToList();

        return DeduplicateProjects(projectFiles);
    }

    private static IEnumerable<DiscoveredProject> ParseSolution(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException($"Invalid solution path: {solutionPath}");

        foreach (var line in File.ReadAllLines(solutionPath))
        {
            var match = SolutionProjectLineRegex.Match(line.Trim());
            if (!match.Success)
                continue;

            var projectName = match.Groups[1].Value;
            var projectRelativePath = match.Groups[2].Value
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            // 跳过 solution folder 等非 csproj 条目
            if (!projectRelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;

            var projectFilePath = Path.GetFullPath(Path.Combine(solutionDirectory, projectRelativePath));
            if (!File.Exists(projectFilePath))
                continue;

            var projectDirectory = Path.GetDirectoryName(projectFilePath)
                ?? throw new InvalidOperationException($"Invalid project path: {projectFilePath}");

            yield return new DiscoveredProject(projectName, projectFilePath, projectDirectory);
        }
    }

    private static DiscoveredProject ToDiscoveredProject(string projectFilePath)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        var projectDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Invalid project path: {fullPath}");

        return new DiscoveredProject(Path.GetFileNameWithoutExtension(fullPath), fullPath, projectDirectory);
    }

    /// <summary>同一 csproj 被多个 sln 引用时只保留一份。</summary>
    private static IReadOnlyList<DiscoveredProject> DeduplicateProjects(IEnumerable<DiscoveredProject> projects) =>
        projects
            .GroupBy(p => p.ProjectFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.ProjectFilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> EnumerateFiles(string rootPath, string pattern) =>
        Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
            .Where(path => !IsUnderExcludedDirectory(path, rootPath));

    private static bool IsUnderExcludedDirectory(string filePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (ExcludedDirectoryNames.Contains(segment))
                return true;
        }

        return false;
    }
}

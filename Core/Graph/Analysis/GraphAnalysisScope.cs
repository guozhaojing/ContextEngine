namespace Core.Graph.Analysis;

/// <summary>
/// 分析执行范围，用于全量或按文件增量重算。
/// </summary>
public sealed class GraphAnalysisScope
{
    public bool IsFullScan { get; init; } = true;

    public IReadOnlySet<string> ChangedRelativeFilePaths { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static GraphAnalysisScope Full() => new() { IsFullScan = true };

    public static GraphAnalysisScope ForFiles(IEnumerable<string> relativeFilePaths) =>
        new()
        {
            IsFullScan = false,
            ChangedRelativeFilePaths = new HashSet<string>(
                relativeFilePaths.Select(NormalizeFilePath),
                StringComparer.OrdinalIgnoreCase)
        };

    public bool ShouldAnalyzeFile(string relativeFilePath)
    {
        if (IsFullScan)
            return true;

        return ChangedRelativeFilePaths.Contains(NormalizeFilePath(relativeFilePath));
    }

    public static string NormalizeFilePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}

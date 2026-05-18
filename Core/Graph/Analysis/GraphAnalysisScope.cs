// =============================================================================
// Graph/Analysis/GraphAnalysisScope.cs — 分析执行范围（全量 / 增量按文件）
// =============================================================================

namespace Core.Graph.Analysis;

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

// =============================================================================
// Scanning/SolutionScanResult.cs — 一次完整扫描的结果容器
// =============================================================================

using Core.Models;

namespace Core.Scanning;

/// <summary>
/// 对整个解决方案（或目录）扫描后的汇总结果。
/// </summary>
public class SolutionScanResult
{
    /// <summary>扫描根目录（用户传入的路径规范化后）。</summary>
    public string ScanRoot { get; set; } = "";

    /// <summary>按项目分组的 CodeUnit 列表。</summary>
    public List<ProjectScanGroup> Projects { get; set; } = new();

    public int TotalCodeUnits => Projects.Sum(p => p.CodeUnits.Count);

    /// <summary>扁平化的所有方法单元，建图时使用。</summary>
    public IReadOnlyList<CodeUnit> AllCodeUnits =>
        Projects.SelectMany(p => p.CodeUnits).ToList();
}

/// <summary>
/// 单个 .csproj 项目内的扫描结果。
/// </summary>
public class ProjectScanGroup
{
    public string ProjectName { get; set; } = "";

    public string ProjectPath { get; set; } = "";

    public List<CodeUnit> CodeUnits { get; set; } = new();
}

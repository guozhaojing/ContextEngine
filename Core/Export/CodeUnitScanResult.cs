// =============================================================================
// Export/CodeUnitScanResult.cs — 扫描结果 JSON 的 DTO 结构
// =============================================================================

namespace Core.Export;

/// <summary>单个方法的导出形状（className / methodName / calls）。</summary>
public class CodeUnitScanResult
{
    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    public List<string> Calls { get; set; } = new();
}

public class ProjectScanExport
{
    public string ProjectName { get; set; } = "";

    public string ProjectPath { get; set; } = "";

    public List<CodeUnitScanResult> Items { get; set; } = new();
}

public class SolutionScanExport
{
    public string ScanRoot { get; set; } = "";

    public string GeneratedAt { get; set; } = "";

    public int ProjectCount { get; set; }

    public int CodeUnitCount { get; set; }

    public List<ProjectScanExport> Projects { get; set; } = new();
}

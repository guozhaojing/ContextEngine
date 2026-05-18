namespace Core.Export;

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

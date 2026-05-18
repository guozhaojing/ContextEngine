using Core.Models;

namespace Core.Scanning;

public class SolutionScanResult
{
    public string ScanRoot { get; set; } = "";

    public List<ProjectScanGroup> Projects { get; set; } = new();

    public int TotalCodeUnits => Projects.Sum(p => p.CodeUnits.Count);

    public IReadOnlyList<CodeUnit> AllCodeUnits =>
        Projects.SelectMany(p => p.CodeUnits).ToList();
}

public class ProjectScanGroup
{
    public string ProjectName { get; set; } = "";

    public string ProjectPath { get; set; } = "";

    public List<CodeUnit> CodeUnits { get; set; } = new();
}

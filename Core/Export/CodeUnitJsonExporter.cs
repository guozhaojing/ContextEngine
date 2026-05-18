// =============================================================================
// Export/CodeUnitJsonExporter.cs — 将扫描结果导出为 scan-*.json
// =============================================================================

using System.Text.Json;
using Core.Models;
using Core.Scanning;

namespace Core.Export;

public static class CodeUnitJsonExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static CodeUnitScanResult ToResult(CodeUnit unit) => new()
    {
        ClassName = unit.ClassName,
        MethodName = unit.MethodName,
        Calls = unit.Calls
    };

    public static SolutionScanExport ToExport(SolutionScanResult scan) => new()
    {
        ScanRoot = scan.ScanRoot,
        GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ProjectCount = scan.Projects.Count,
        CodeUnitCount = scan.TotalCodeUnits,
        Projects = scan.Projects.Select(p => new ProjectScanExport
        {
            ProjectName = p.ProjectName,
            ProjectPath = p.ProjectPath,
            Items = p.CodeUnits.Select(ToResult).ToList()
        }).ToList()
    };

    public static async Task<string> SaveAsync(
        SolutionScanResult scan,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        outputDirectory ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var fileName = $"scan-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var outputPath = Path.Combine(outputDirectory, fileName);

        var json = JsonSerializer.Serialize(ToExport(scan), SerializerOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        return Path.GetFullPath(outputPath);
    }

    public static string FormatOne(CodeUnit unit) =>
        JsonSerializer.Serialize(ToResult(unit), SerializerOptions);
}

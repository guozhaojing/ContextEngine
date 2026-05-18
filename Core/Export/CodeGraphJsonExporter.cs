using System.Text.Json;
using Core.Graph;

namespace Core.Export;

public static class CodeGraphJsonExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<string> SaveAsync(
        CodeGraph graph,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        outputDirectory ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var fileName = $"graph-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var outputPath = Path.Combine(outputDirectory, fileName);

        var export = new
        {
            scanRoot = graph.ScanRoot,
            schemaVersion = graph.SchemaVersion,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            nodeCount = graph.Nodes.Count,
            edgeCount = graph.Edges.Count,
            resolvedEdgeCount = graph.ResolvedEdgeCount,
            externalNodeCount = graph.ExternalNodeCount,
            nodes = graph.Nodes.Select(n => new
            {
                n.Id,
                n.Label,
                n.ProjectName,
                n.ProjectPath,
                n.Namespace,
                n.ClassName,
                n.MethodName,
                n.IsExternal,
                calledBy = n.CalledBy,
                attributes = n.Attributes
            }),
            edges = graph.Edges.Select(e => new
            {
                from = e.FromId,
                to = e.ToId,
                call = e.Call,
                kind = e.Kind,
                resolved = e.IsResolved,
                attributes = e.Attributes
            })
        };

        var json = JsonSerializer.Serialize(export, SerializerOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        return Path.GetFullPath(outputPath);
    }
}

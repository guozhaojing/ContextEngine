// =============================================================================
// Export/ContextExportService.cs — export StructuredContext to context.json
// =============================================================================

using System.Text.Json;
using Core.Context.Models;

namespace Core.Context.Export;

public sealed class ContextExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string> SaveAsync(
        StructuredContext context,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var export = new
        {
            schemaVersion = 1,
            generatedAt = now,
            context.Query,
            context.Intent,
            context.Summary,
            context.SemanticPaths,
            context.Routes,
            context.Entities,
            context.Tables,
            context.BusinessRules,
            context.CompressedMethods,
            context.TokenEstimate,
            context.Metadata,
            budget = new
            {
                total = context.Metadata.GetValueOrDefault("budget_total", "?"),
                allocated = context.Metadata.GetValueOrDefault("budget_allocated", "?"),
                remaining = context.Metadata.GetValueOrDefault("budget_remaining", "?"),
                semanticCompression = context.Metadata.GetValueOrDefault("semantic_compression", "?"),
                redundancyReduction = context.Metadata.GetValueOrDefault("redundancy_reduction", "?"),
                routes = context.Metadata.GetValueOrDefault("budget_routes_used", "?"),
                semanticPaths = context.Metadata.GetValueOrDefault("budget_semantic_paths_used", "?"),
                businessRules = context.Metadata.GetValueOrDefault("budget_business_rules_used", "?"),
                methods = context.Metadata.GetValueOrDefault("budget_methods_used", "?"),
                metadata = context.Metadata.GetValueOrDefault("budget_metadata_used", "?")
            }
        };

        var outputPath = Path.Combine(outputDirectory, "context.json");
        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        return Path.GetFullPath(outputPath);
    }

    public string Serialize(StructuredContext context)
    {
        var export = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            context.Query,
            context.Intent,
            context.Summary,
            context.SemanticPaths,
            context.Routes,
            context.Entities,
            context.Tables,
            context.BusinessRules,
            context.CompressedMethods,
            context.TokenEstimate,
            context.Metadata
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }
}

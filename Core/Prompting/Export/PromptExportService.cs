// =============================================================================
// Export/PromptExportService.cs — exports prompt context to files
// =============================================================================

using System.Text.Json;
using Core.Prompting.Models;

namespace Core.Prompting.Export;

public sealed class PromptExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Templates.MarkdownPromptFormatter _formatter;

    public PromptExportService(PromptAssemblyOptions? options = null)
    {
        _formatter = new Templates.MarkdownPromptFormatter(options);
    }

    public async Task<string> SaveMarkdownAsync(
        PromptContext context,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var markdown = _formatter.Format(context);
        var outputPath = Path.Combine(outputDirectory, "prompt-context.md");

        await File.WriteAllTextAsync(outputPath, markdown, ct);
        return Path.GetFullPath(outputPath);
    }

    public async Task<string> SaveJsonAsync(
        PromptContext context,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "prompt-context.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            context.UserQuery,
            context.DetectedIntent,
            context.Summary,
            reasoningSections = context.ReasoningSections.Select(s => new
            {
                s.SectionId,
                s.Title,
                kind = s.Kind.ToString(),
                s.Priority,
                s.TokenEstimate,
                s.RelevanceScore,
                s.CompressionRatio,
                s.Content,
                s.SourceChunkIds
            }),
            context.SemanticPaths,
            importantMethods = context.ImportantMethods,
            context.Entities,
            context.Tables,
            context.BusinessRules,
            context.Constraints,
            missingContextIssues = context.MissingContextIssues.Select(i => new
            {
                i.IssueId,
                kind = i.Kind.ToString(),
                i.Description,
                i.AffectedEntity,
                i.AffectedRoute,
                i.AffectedMethod,
                i.Severity,
                i.Recommendation
            }),
            context.TokenEstimate,
            context.Metadata
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);

        return Path.GetFullPath(outputPath);
    }

    public string Serialize(PromptContext context)
    {
        var export = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            context.UserQuery,
            context.DetectedIntent,
            context.Summary,
            reasoningSections = context.ReasoningSections.Select(s => new
            {
                s.SectionId,
                s.Title,
                kind = s.Kind.ToString(),
                s.Priority,
                s.TokenEstimate,
                s.Content
            }),
            context.SemanticPaths,
            importantMethods = context.ImportantMethods,
            context.Entities,
            context.Tables,
            context.BusinessRules,
            context.Constraints,
            missingContextIssues = context.MissingContextIssues.Select(i => new
            {
                i.IssueId,
                kind = i.Kind.ToString(),
                i.Description,
                i.Severity
            }),
            context.TokenEstimate,
            context.Metadata
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }
}

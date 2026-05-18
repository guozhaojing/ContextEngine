using System.Text.Json;
using Core.Context.Models;

namespace Core.Context;

public sealed class PromptContextExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string> SaveMarkdownAsync(
        ContextDocument doc,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var markdown = ContextFormatting.ToMarkdown(doc);
        var fileName = $"context-{SanitizeFileName(doc.Id)}.md";
        var outputPath = Path.Combine(outputDirectory, fileName);

        await File.WriteAllTextAsync(outputPath, markdown, ct);
        return Path.GetFullPath(outputPath);
    }

    public async Task<string> SaveJsonAsync(
        ContextDocument doc,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var fileName = $"context-{SanitizeFileName(doc.Id)}.json";
        var outputPath = Path.Combine(outputDirectory, fileName);

        var export = new
        {
            schemaVersion = 2,
            generatedAt = doc.GeneratedAt,
            doc.Id,
            doc.Query,
            budgetMax = doc.BudgetMax,
            budgetUsed = doc.BudgetUsed,
            totalTokens = doc.TotalTokens,
            sourceResultCount = doc.SourceResultCount,
            sections = doc.Sections.Select(s => new
            {
                s.Title,
                s.Kind,
                s.Priority,
                s.TokenCount,
                s.CompressionRatio,
                s.RelevanceScore,
                s.Content,
                s.SourceChunkIds
            })
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);
        return Path.GetFullPath(outputPath);
    }

    public async Task<string> SaveStructuredContextAsync(
        StructuredContext context,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var export = new
        {
            schemaVersion = 2,
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

        var outputPath = Path.Combine(outputDirectory, "context.json");
        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);

        return Path.GetFullPath(outputPath);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }
}

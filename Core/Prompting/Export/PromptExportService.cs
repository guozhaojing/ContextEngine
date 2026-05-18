// =============================================================================
// Export/PromptExportService.cs — exports prompt context to files
// =============================================================================

using System.Text.Json;
using Core.Prompting.Models;
using Core.Prompting.QueryExecution;

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

    // ═══════════════════════════════════════════════════════════════
    // PromptContext exports (Phase 6B)
    // ═══════════════════════════════════════════════════════════════

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
        var json = Serialize(context);
        await File.WriteAllTextAsync(outputPath, json, ct);

        return Path.GetFullPath(outputPath);
    }

    // ═══════════════════════════════════════════════════════════════
    // Orchestration exports (Phase 6C)
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> SaveFinalPromptAsync(
        FinalPrompt finalPrompt,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "final-prompt.md");
        await File.WriteAllTextAsync(outputPath, finalPrompt.Content, ct);
        return Path.GetFullPath(outputPath);
    }

    public async Task<string> SaveTraceAsync(
        PromptTrace trace,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "prompt-trace.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = trace.GeneratedAt,
            trace.TraceId,
            trace.Query,
            totalSteps = trace.TotalSteps,
            totalElapsedMs = trace.TotalElapsedMs,
            hasErrors = trace.HasErrors,
            steps = trace.Steps.Select(s => new
            {
                s.StepId,
                s.Phase,
                s.Description,
                status = s.Status.ToString(),
                s.ElapsedMs,
                s.Details
            })
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);
        return Path.GetFullPath(outputPath);
    }

    public async Task<string> SavePolicyAsync(
        ContextPolicies.ContextPolicy policy,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "context-policy.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            policy.PolicyId,
            policy.PolicyName,
            policy.Description,
            policy.MaxTokens,
            policy.FocusAreas,
            policy.IncludeMissingContext,
            policy.IncludeExecutionPlan,
            policy.OutputFormatHint,
            sectionRules = policy.SectionRules.Select(r => new
            {
                kind = r.SectionKind.ToString(),
                r.Required,
                r.MaxItems,
                r.MaxTokens,
                r.MinRelevance,
                r.Order
            })
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);
        return Path.GetFullPath(outputPath);
    }

    public async Task SaveOrchestrationResultAsync(
        OrchestrationResult result,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "export");
        Directory.CreateDirectory(outputDirectory);

        await SaveFinalPromptAsync(result.FinalPrompt, outputDirectory, ct);
        await SaveTraceAsync(result.Trace, outputDirectory, ct);
        await SavePolicyAsync(result.Policy, outputDirectory, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Serialization
    // ═══════════════════════════════════════════════════════════════

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

        return JsonSerializer.Serialize(export, JsonOptions);
    }
}

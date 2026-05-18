// =============================================================================
// Systems/ContextDriftDetector.cs — detects inconsistencies across pipeline stages
// =============================================================================
// 【检测】
// - Retrieval 结果是否在 Context 中丢失
// - Entity/Table 是否在压缩中被错误去除
// - Path 是否在 prompt 组装中断裂
// - Chunk 覆盖率
// =============================================================================

using Core.Context.Models;
using Core.Prompting.Models;
using Core.Retrieval.Retrieval;

namespace Core.Evaluation.Systems;

public sealed class ContextDriftDetector
{
    public DriftDetectionResult Detect(
        StructuredContext structuredContext,
        PromptContext promptContext,
        RetrievalResult retrievalResult)
    {
        var issues = new List<DriftIssue>();

        DetectChunkLoss(structuredContext, retrievalResult, issues);
        DetectEntityDrift(structuredContext, promptContext, issues);
        DetectPathIntegrity(structuredContext, promptContext, issues);
        DetectTableDrift(structuredContext, promptContext, issues);
        DetectMethodDrift(structuredContext, promptContext, issues);

        return new DriftDetectionResult
        {
            ContextQuery = structuredContext.Query,
            Issues = issues,
            DriftDetected = issues.Count > 0,
            TotalIssues = issues.Count,
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private static void DetectChunkLoss(
        StructuredContext context,
        RetrievalResult retrieval,
        List<DriftIssue> issues)
    {
        var totalChunks = retrieval.Candidates.Count;
        if (totalChunks == 0) return;

        var referencedChunks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in retrieval.Candidates)
    {
            referencedChunks.Add(c.Chunk.ChunkId);
        }

        var contextItems = context.SemanticPaths.Count + context.Routes.Count +
                           context.Entities.Count + context.Tables.Count +
                           context.BusinessRules.Count + context.CompressedMethods.Count;

        if (contextItems == 0 && totalChunks > 0)
        {
            issues.Add(new DriftIssue
            {
                IssueType = "CompleteLoss",
                Description = $"All {totalChunks} retrieval candidates were lost — no context items produced.",
                Severity = 1.0
            });
        }

        var coverageRatio = contextItems > 0 ? Math.Min(1.0, (double)contextItems / totalChunks) : 0;
        if (totalChunks > 5 && coverageRatio < 0.1)
        {
            issues.Add(new DriftIssue
            {
                IssueType = "HighCompression",
                Description = $"Only {contextItems} context items from {totalChunks} retrieval candidates (compression ratio: {coverageRatio:P0}).",
                Severity = 0.7
            });
        }
    }

    private static void DetectEntityDrift(
        StructuredContext context,
        PromptContext prompt,
        List<DriftIssue> issues)
    {
        var contextEntities = new HashSet<string>(context.Entities, StringComparer.OrdinalIgnoreCase);
        var promptEntities = new HashSet<string>(prompt.Entities, StringComparer.OrdinalIgnoreCase);

        var lost = contextEntities.Except(promptEntities).ToList();
        if (lost.Count > 0 && contextEntities.Count > 0)
        {
            var ratio = (double)lost.Count / contextEntities.Count;
            if (ratio > 0.5)
            {
                issues.Add(new DriftIssue
                {
                    IssueType = "EntityDrift",
                    Description = $"{lost.Count}/{contextEntities.Count} entities lost between Context and Prompt stages: {string.Join(", ", lost.Take(5))}",
                    Severity = Math.Min(0.9, 0.4 + ratio)
                });
            }
        }
    }

    private static void DetectPathIntegrity(
        StructuredContext context,
        PromptContext prompt,
        List<DriftIssue> issues)
    {
        if (context.SemanticPaths.Count > 0 && prompt.SemanticPaths.Count == 0)
        {
            issues.Add(new DriftIssue
            {
                IssueType = "PathBreak",
                Description = $"All {context.SemanticPaths.Count} semantic paths were lost in prompt assembly.",
                Severity = 0.8
            });
        }

        if (context.SemanticPaths.Count > 3 && prompt.SemanticPaths.Count < context.SemanticPaths.Count / 2)
        {
            issues.Add(new DriftIssue
            {
                IssueType = "PathTruncation",
                Description = $"Semantic paths reduced from {context.SemanticPaths.Count} to {prompt.SemanticPaths.Count} ({(double)prompt.SemanticPaths.Count / context.SemanticPaths.Count:P0} preserved).",
                Severity = 0.5
            });
        }
    }

    private static void DetectTableDrift(
        StructuredContext context,
        PromptContext prompt,
        List<DriftIssue> issues)
    {
        var contextTables = new HashSet<string>(context.Tables, StringComparer.OrdinalIgnoreCase);
        var promptTables = new HashSet<string>(prompt.Tables, StringComparer.OrdinalIgnoreCase);

        var lost = contextTables.Except(promptTables).ToList();
        if (lost.Count > 0 && contextTables.Count > 0)
        {
            var ratio = (double)lost.Count / contextTables.Count;
            if (ratio > 0.5)
            {
                issues.Add(new DriftIssue
                {
                    IssueType = "TableDrift",
                    Description = $"{lost.Count}/{contextTables.Count} tables lost: {string.Join(", ", lost.Take(5))}",
                    Severity = Math.Min(0.8, 0.3 + ratio)
                });
            }
        }
    }

    private static void DetectMethodDrift(
        StructuredContext context,
        PromptContext prompt,
        List<DriftIssue> issues)
    {
        if (context.CompressedMethods.Count > 0 && prompt.ImportantMethods.Count == 0)
        {
            issues.Add(new DriftIssue
            {
                IssueType = "MethodDrift",
                Description = $"All {context.CompressedMethods.Count} compressed methods lost in prompt assembly.",
                Severity = 0.7
            });
        }
    }
}

public sealed class DriftDetectionResult
{
    public string ContextQuery { get; init; } = "";
    public required IReadOnlyList<DriftIssue> Issues { get; init; }
    public bool DriftDetected { get; init; }
    public int TotalIssues { get; init; }
    public string GeneratedAt { get; init; } = "";
}

public sealed class DriftIssue
{
    public required string IssueType { get; init; }
    public required string Description { get; init; }
    public double Severity { get; init; }
}

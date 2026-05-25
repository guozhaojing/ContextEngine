// =============================================================================
// Cognition/CodeFix/TaskDatasetRunner.cs — batch execution + auto stats
// =============================================================================
// Purpose: Run 100 real L1 modification tasks, collect stats, save results.
// Output: per-task JSON + aggregate statistics + success/failure matrix.
// =============================================================================

using System.Text.Json;
using Core.Graph;

namespace Core.Cognition.CodeFix;

public sealed class TaskDatasetRunner
{
    private readonly GraphQueryService _graphQuery;
    private readonly ContextExtractorV2 _extractorV2;
    private readonly CodeFixPipeline _pipeline;
    private readonly string _outputDir;
    private readonly List<TaskExecutionResult> _results = new();
    private readonly DatasetOptions _options;

    public TaskDatasetRunner(GraphQueryService graphQuery, string? outputDir = null, DatasetOptions? options = null)
    {
        _graphQuery = graphQuery;
        _extractorV2 = new ContextExtractorV2(graphQuery);
        _pipeline = new CodeFixPipeline(graphQuery);
        _outputDir = outputDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContextEngine", "task-dataset");
        _options = options ?? DatasetOptions.Default;
        Directory.CreateDirectory(_outputDir);
    }

    public async Task<DatasetResult> RunBatchAsync(
        IReadOnlyList<FixTask> tasks,
        Func<string, Task<string>> llmGenerator,
        IProgress<int>? progress = null)
    {
        _results.Clear();
        var startedAt = DateTime.UtcNow;

        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var result = await ExecuteTaskAsync(task, llmGenerator);
            _results.Add(result);
            progress?.Report(i + 1);
        }

        var completedAt = DateTime.UtcNow;
        var stats = ComputeBatchStats();

        var datasetResult = new DatasetResult
        {
            DatasetName = $"dataset-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            TotalTasks = tasks.Count,
            ExecutedCount = _results.Count,
            Stats = stats,
            Results = _results.AsReadOnly(),
        };

        SaveDatasetResult(datasetResult);
        SaveFullReport(datasetResult);

        return datasetResult;
    }

    private async Task<TaskExecutionResult> ExecuteTaskAsync(
        FixTask task,
        Func<string, Task<string>> llmGenerator)
    {
        var startedAt = DateTime.UtcNow;

        // Locate target
        var locator = new SymbolLocator(_graphQuery);
        var symbols = locator.Locate(new CodeFixRequest
        {
            Query = task.TargetMethod,
            TargetFilePath = task.TargetFile,
            TargetMethodName = task.TargetMethod,
        });

        if (symbols.Count == 0)
        {
            return new TaskExecutionResult
            {
                TaskId = task.TaskId,
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                BuildSuccess = false,
                FirstPassSuccess = false,
                AttemptCount = 0,
                FailureCategory = FailureCategory.SymbolResolutionFailure,
                FailureDetails = new[] { $"Symbol not found: {task.TargetMethod}" },
                CompileErrors = Array.Empty<string>(),
                ContextSizeChars = 0,
                DurationMs = 0,
            };
        }

        var target = symbols[0];

        // Block public API
        if (target.IsPublicApi && _options.SkipPublicApi)
        {
            return new TaskExecutionResult
            {
                TaskId = task.TaskId,
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                BuildSuccess = false,
                FirstPassSuccess = false,
                AttemptCount = 0,
                FailureCategory = FailureCategory.EntryPointRegression,
                FailureDetails = new[] { $"Skipped: public API {target.MethodName}" },
                CompileErrors = Array.Empty<string>(),
                ContextSizeChars = 0,
                DurationMs = 0,
            };
        }

        // Extract V2 context
        var context = _extractorV2.Extract(target);
        var contextText = _extractorV2.FormatForLLM(context);

        // Execute pipeline
        var request = new CodeFixRequest
        {
            Query = task.TargetMethod,
            Task = task.ModificationGoal,
            TargetFilePath = task.TargetFile,
            TargetMethodName = task.TargetMethod,
            RepositoryPath = task.ProjectPath,
            MaxRetries = task.MaxRetries,
        };

        var fixResult = await _pipeline.ExecuteAsync(request, llmGenerator);
        var completedAt = DateTime.UtcNow;

        var compileErrors = fixResult.FinalBuild?.Errors
            .Select(e => e.ToContextString()).ToList() ?? new List<string>();

        var failureCategory = fixResult.Success ? null
            : ClassifyBatchFailure(fixResult, compileErrors);

        var firstPassSuccess = fixResult.Attempts == 1 && fixResult.Success;
        var patchContent = fixResult.Patches.LastOrDefault()?.Diff ?? "";

        return new TaskExecutionResult
        {
            TaskId = task.TaskId,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            BuildSuccess = fixResult.Success,
            FirstPassSuccess = firstPassSuccess,
            AttemptCount = fixResult.Attempts,
            GeneratedPatch = patchContent,
            CompileErrors = compileErrors,
            FailureCategory = failureCategory,
            FailureDetails = fixResult.RepairHistory,
            ContextSizeChars = context.ContextSizeChars,
            DurationMs = (completedAt - startedAt).TotalMilliseconds,
        };
    }

    private BatchStats ComputeBatchStats()
    {
        var byCategory = new Dictionary<FailureCategory, int>();
        foreach (var r in _results)
        {
            if (r.FailureCategory is not null)
                byCategory[r.FailureCategory.Value] = byCategory.GetValueOrDefault(r.FailureCategory.Value, 0) + 1;
        }

        var successResults = _results.Where(r => r.BuildSuccess).ToList();
        var failResults = _results.Where(r => !r.BuildSuccess).ToList();

        return new BatchStats
        {
            BuildSuccessRate = _results.Count > 0 ? (double)successResults.Count / _results.Count : 0,
            FirstPassRate = _results.Count > 0 ? (double)_results.Count(r => r.FirstPassSuccess) / _results.Count : 0,
            RetryRecoveryRate = _results.Count(r => !r.FirstPassSuccess) > 0
                ? (double)_results.Count(r => r.BuildSuccess && !r.FirstPassSuccess) / _results.Count(r => !r.FirstPassSuccess) : 0,
            SymbolResolutionFailureRate = _results.Count > 0
                ? (double)_results.Count(r => r.FailureCategory == FailureCategory.SymbolResolutionFailure) / _results.Count : 0,
            HallucinationRate = _results.Count > 0
                ? (double)_results.Count(r => r.FailureCategory == FailureCategory.HallucinatedAPI) / _results.Count : 0,
            CompileFailureRate = _results.Count > 0
                ? (double)_results.Count(r => r.FailureCategory == FailureCategory.CompileFailure) / _results.Count : 0,

            AvgContextSizeSuccess = successResults.Count > 0 ? successResults.Average(r => r.ContextSizeChars) : 0,
            AvgContextSizeFail = failResults.Count > 0 ? failResults.Average(r => r.ContextSizeChars) : 0,
            AvgAttempts = _results.Count > 0 ? _results.Average(r => r.AttemptCount) : 0,
            AvgDurationMs = _results.Count > 0 ? _results.Average(r => r.DurationMs) : 0,

            FailuresByCategory = byCategory,
        };
    }

    private static FailureCategory? ClassifyBatchFailure(CodeFixResult fixResult, List<string> compileErrors)
    {
        if (fixResult.Attempts == 0) return FailureCategory.SymbolResolutionFailure;

        if (compileErrors.Count > 0)
        {
            foreach (var err in compileErrors)
            {
                if (err.Contains("does not contain", StringComparison.OrdinalIgnoreCase)
                    || err.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || err.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                    return FailureCategory.HallucinatedAPI;
            }
            return FailureCategory.CompileFailure;
        }

        foreach (var h in fixResult.RepairHistory)
        {
            if (h.Contains("REJECTED", StringComparison.Ordinal) && h.Contains("signature", StringComparison.OrdinalIgnoreCase))
                return FailureCategory.OverModification;
        }

        return FailureCategory.MissingContext;
    }

    private void SaveDatasetResult(DatasetResult result)
    {
        var json = JsonSerializer.Serialize(new
        {
            result.DatasetName,
            result.TotalTasks,
            result.ExecutedCount,
            stats = result.Stats,
            tasks = _results.Select(r => new
            {
                r.TaskId,
                r.BuildSuccess,
                r.FirstPassSuccess,
                r.AttemptCount,
                failureCategory = r.FailureCategory?.ToString(),
                r.ContextSizeChars,
                r.DurationMs,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(Path.Combine(_outputDir, $"{result.DatasetName}.json"), json);
    }

    private void SaveFullReport(DatasetResult result)
    {
        var report = result.GenerateReport();
        File.WriteAllText(Path.Combine(_outputDir, $"{result.DatasetName}-report.md"), report);
    }
}

public class DatasetOptions
{
    public bool SkipPublicApi { get; init; } = true;
    public bool SavePerTaskResults { get; init; } = true;

    public static DatasetOptions Default => new();
}

public sealed class DatasetResult
{
    public required string DatasetName { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
    public int TotalTasks { get; init; }
    public int ExecutedCount { get; init; }
    public required BatchStats Stats { get; init; }
    public required IReadOnlyList<TaskExecutionResult> Results { get; init; }

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# 任务批量执行报告: {DatasetName}");
        sb.AppendLine();
        sb.AppendLine($"开始: {StartedAt:O}");
        sb.AppendLine($"完成: {CompletedAt:O}");
        sb.AppendLine($"耗时: {(CompletedAt - StartedAt).TotalMinutes:F1} 分钟");
        sb.AppendLine();

        sb.AppendLine($"## 总体统计");
        sb.AppendLine();
        sb.AppendLine($"| 指标 | 值 |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| 总任务数 | {TotalTasks} |");
        sb.AppendLine($"| 构建成功率 | {Stats.BuildSuccessRate:P1} |");
        sb.AppendLine($"| 首次通过率 | {Stats.FirstPassRate:P1} |");
        sb.AppendLine($"| 重试恢复率 | {Stats.RetryRecoveryRate:P1} |");
        sb.AppendLine($"| 符号解析失败率 | {Stats.SymbolResolutionFailureRate:P1} |");
        sb.AppendLine($"| 幻觉率 | {Stats.HallucinationRate:P1} |");
        sb.AppendLine($"| 编译失败率 | {Stats.CompileFailureRate:P1} |");
        sb.AppendLine($"| 平均尝试次数 | {Stats.AvgAttempts:F1} |");
        sb.AppendLine($"| 平均耗时 | {Stats.AvgDurationMs:F0}ms |");
        sb.AppendLine();

        sb.AppendLine($"## 上下文分析");
        sb.AppendLine();
        sb.AppendLine($"| 指标 | 值 |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| 成功任务平均上下文 | {Stats.AvgContextSizeSuccess:F0} 字符 |");
        sb.AppendLine($"| 失败任务平均上下文 | {Stats.AvgContextSizeFail:F0} 字符 |");
        sb.AppendLine();

        sb.AppendLine("## 失败分类");
        sb.AppendLine();
        foreach (var kvp in Stats.FailuresByCategory.OrderByDescending(kvp => kvp.Value))
            sb.AppendLine($"- {kvp.Key}: {kvp.Value}");

        return sb.ToString();
    }
}

public sealed class BatchStats
{
    public double BuildSuccessRate { get; init; }
    public double FirstPassRate { get; init; }
    public double RetryRecoveryRate { get; init; }
    public double SymbolResolutionFailureRate { get; init; }
    public double HallucinationRate { get; init; }
    public double CompileFailureRate { get; init; }
    public double AvgContextSizeSuccess { get; init; }
    public double AvgContextSizeFail { get; init; }
    public double AvgAttempts { get; init; }
    public double AvgDurationMs { get; init; }
    public required IReadOnlyDictionary<FailureCategory, int> FailuresByCategory { get; init; }
}

// =============================================================================
// Cognition/CodeFix/TaskExecutionRunner.cs — execute + record + compute stats
// =============================================================================
using System.Diagnostics;
using System.Text.Json;
using Core.Graph;

namespace Core.Cognition.CodeFix;

public sealed class TaskExecutionRunner
{
    private readonly CodeFixPipeline _pipeline;
    private readonly List<TaskExecutionResult> _results = new();
    private readonly string _statsDir;

    public TaskExecutionRunner(GraphQueryService graphQuery, string? statsDir = null)
    {
        _pipeline = new CodeFixPipeline(graphQuery);
        _statsDir = statsDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContextEngine", "fix-stats");
        Directory.CreateDirectory(_statsDir);
    }

    public IReadOnlyList<TaskExecutionResult> Results => _results.AsReadOnly();

    public async Task<TaskExecutionResult> ExecuteAsync(
        FixTask task,
        Func<string, Task<string>> llmGenerator)
    {
        var startedAt = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

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
        sw.Stop();

        var compileErrors = fixResult.FinalBuild?.Errors
            .Select(e => e.ToContextString()).ToList()
            ?? new List<string>();

        var failureDetails = new List<string>();
        var failureCategory = ClassifyFailure(fixResult, task, compileErrors);

        // Determine first-pass success
        var firstPassSuccess = fixResult.Attempts == 1 && fixResult.Success;

        // Extract patch content
        var patchContent = fixResult.Patches.LastOrDefault()?.Diff ?? "";
        var contextSize = patchContent.Length;

        var result = new TaskExecutionResult
        {
            TaskId = task.TaskId,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            BuildSuccess = fixResult.Success,
            FirstPassSuccess = firstPassSuccess,
            AttemptCount = fixResult.Attempts,
            GeneratedPatch = patchContent,
            CompileErrors = compileErrors,
            FailureCategory = fixResult.Success ? null : failureCategory,
            FailureDetails = failureDetails,
            ContextSizeChars = contextSize,
            DurationMs = sw.Elapsed.TotalMilliseconds,
        };

        _results.Add(result);
        SaveResult(task, result);

        return result;
    }

    public ExecutionStats ComputeStats()
    {
        var byCategory = new Dictionary<FailureCategory, int>();
        foreach (var r in _results)
        {
            if (r.FailureCategory is not null)
            {
                var cat = r.FailureCategory.Value;
                byCategory[cat] = byCategory.GetValueOrDefault(cat, 0) + 1;
            }
        }

        var totalRetries = _results.Sum(r => r.AttemptCount);
        var totalAttempts = _results.Count > 0 ? (double)totalRetries / _results.Count : 0;

        return new ExecutionStats
        {
            TotalTasks = _results.Count,
            BuildSuccesses = _results.Count(r => r.BuildSuccess),
            FirstPassSuccesses = _results.Count(r => r.FirstPassSuccess),
            RetryRecoveries = _results.Count(r => r.BuildSuccess && !r.FirstPassSuccess),
            TotalFailures = _results.Count(r => !r.BuildSuccess),
            AverageContextSizeChars = _results.Count > 0
                ? _results.Average(r => r.ContextSizeChars) : 0,
            HallucinationCount = _results.Count(r =>
                r.FailureCategory == FailureCategory.HallucinatedAPI),
            SymbolResolutionFailureCount = _results.Count(r =>
                r.FailureCategory == FailureCategory.SymbolResolutionFailure),
            CompileFailureCount = _results.Count(r =>
                r.FailureCategory == FailureCategory.CompileFailure),
            FailuresByCategory = byCategory,
        };
    }

    private static FailureCategory? ClassifyFailure(
        CodeFixResult fixResult,
        FixTask task,
        List<string> compileErrors)
    {
        if (fixResult.Success) return null;

        if (fixResult.Attempts == 0)
            return FailureCategory.SymbolResolutionFailure;

        if (compileErrors.Count > 0)
        {
            foreach (var err in compileErrors)
            {
                if (err.Contains("does not contain", StringComparison.OrdinalIgnoreCase)
                    || err.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || err.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                    return FailureCategory.HallucinatedAPI;

                if (err.Contains("CS0", StringComparison.Ordinal)
                    || err.Contains("error", StringComparison.OrdinalIgnoreCase))
                    return FailureCategory.CompileFailure;
            }
            return FailureCategory.CompileFailure;
        }

        foreach (var h in fixResult.RepairHistory)
        {
            if (h.Contains("REJECTED", StringComparison.Ordinal))
            {
                if (h.Contains("signature", StringComparison.OrdinalIgnoreCase))
                    return FailureCategory.OverModification;
                if (h.Contains("public API", StringComparison.OrdinalIgnoreCase))
                    return FailureCategory.EntryPointRegression;
            }
        }

        return FailureCategory.MissingContext;
    }

    private void SaveResult(FixTask task, TaskExecutionResult result)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                taskId = result.TaskId,
                task = task.Description,
                target = task.TargetMethod,
                buildSuccess = result.BuildSuccess,
                firstPass = result.FirstPassSuccess,
                attempts = result.AttemptCount,
                failureCategory = result.FailureCategory?.ToString(),
                compileErrors = result.CompileErrors,
                durationMs = result.DurationMs,
                timestamp = result.CompletedAt.ToString("O"),
            }, new JsonSerializerOptions { WriteIndented = true });

            var filePath = Path.Combine(_statsDir, $"{task.TaskId}.json");
            File.WriteAllText(filePath, json);
        }
        catch { }
    }
}

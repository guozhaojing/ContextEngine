// =============================================================================
// Cognition/CodeFix/ExecutionTypes.cs — task definitions, stats, failure records
// =============================================================================

namespace Core.Cognition.CodeFix;

public sealed class FixTask
{
    public required string TaskId { get; init; }
    public required string Description { get; init; }
    public required string TargetMethod { get; init; }
    public string? TargetFile { get; init; }
    public required string ModificationGoal { get; init; }
    public FixTaskLevel Level { get; init; } = FixTaskLevel.L1_SingleMethod;
    public int MaxRetries { get; init; } = 3;
    public string? ProjectPath { get; init; }
    public required IReadOnlyList<string> ExpectedBehavior { get; init; }
}

public enum FixTaskLevel
{
    L1_SingleMethod = 1,
}

public enum FailureCategory
{
    MissingContext = 0,
    HallucinatedAPI = 1,
    WrongBusinessLogic = 2,
    CompileFailure = 3,
    OverModification = 4,
    SymbolResolutionFailure = 5,
    EntryPointRegression = 6,
}

public static class FailureCategoryExtensions
{
    public static string ToDisplayText(this FailureCategory c) => c switch
    {
        FailureCategory.MissingContext => "缺少上下文 — LLM 没有足够信息",
        FailureCategory.HallucinatedAPI => "幻觉 API — 调用了不存在的类/方法",
        FailureCategory.WrongBusinessLogic => "业务逻辑错误 — 代码编译通过但逻辑不对",
        FailureCategory.CompileFailure => "编译失败 — 语法错误或类型不匹配",
        FailureCategory.OverModification => "过度修改 — 改了不需要改的地方",
        FailureCategory.SymbolResolutionFailure => "符号解析失败 — 找不到目标方法",
        FailureCategory.EntryPointRegression => "入口点回归 — 修改破坏了 API 接口",
        _ => "未知",
    };
}

public sealed class TaskExecutionResult
{
    public required string TaskId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
    public bool BuildSuccess { get; init; }
    public bool FirstPassSuccess { get; init; }
    public int AttemptCount { get; init; }
    public string GeneratedPatch { get; init; } = "";
    public required IReadOnlyList<string> CompileErrors { get; init; }
    public FailureCategory? FailureCategory { get; init; }
    public required IReadOnlyList<string> FailureDetails { get; init; }
    public int ContextSizeChars { get; init; }
    public double DurationMs { get; init; }
}

public sealed class ExecutionStats
{
    public int TotalTasks { get; init; }
    public int BuildSuccesses { get; init; }
    public int FirstPassSuccesses { get; init; }
    public int RetryRecoveries { get; init; }
    public int TotalFailures { get; init; }
    public double BuildSuccessRate => TotalTasks > 0 ? (double)BuildSuccesses / TotalTasks : 0;
    public double FirstPassSuccessRate => TotalTasks > 0 ? (double)FirstPassSuccesses / TotalTasks : 0;
    public double RetryRecoveryRate =>
        TotalTasks > 0 && FirstPassSuccesses < TotalTasks
            ? (double)RetryRecoveries / (TotalTasks - FirstPassSuccesses) : 0;
    public double AverageRetryCount { get; init; }
    public double HallucinationRate => TotalTasks > 0
        ? (double)HallucinationCount / TotalTasks : 0;
    public double AverageContextSizeChars { get; init; }
    public int HallucinationCount { get; init; }
    public int SymbolResolutionFailureCount { get; init; }
    public int CompileFailureCount { get; init; }
    public required IReadOnlyDictionary<FailureCategory, int> FailuresByCategory { get; init; }

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# AI 代码修改统计报告");
        sb.AppendLine();
        sb.AppendLine($"总任务数:          {TotalTasks}");
        sb.AppendLine($"构建成功率:        {BuildSuccessRate:P1} ({BuildSuccesses}/{TotalTasks})");
        sb.AppendLine($"首次通过率:        {FirstPassSuccessRate:P1} ({FirstPassSuccesses}/{TotalTasks})");
        sb.AppendLine($"重试恢复率:        {RetryRecoveryRate:P1} ({RetryRecoveries})");
        sb.AppendLine($"幻觉率:            {HallucinationRate:P1} ({HallucinationCount})");
        sb.AppendLine($"符号解析失败:      {SymbolResolutionFailureCount}");
        sb.AppendLine($"编译失败:          {CompileFailureCount}");
        sb.AppendLine($"平均上下文大小:    {AverageContextSizeChars:F0} 字符");
        sb.AppendLine();
        sb.AppendLine("## 失败分类分布");
        sb.AppendLine();
        foreach (var kvp in FailuresByCategory.OrderByDescending(kvp => kvp.Value))
            sb.AppendLine($"  {kvp.Key.ToDisplayText()}: {kvp.Value}");
        return sb.ToString();
    }
}

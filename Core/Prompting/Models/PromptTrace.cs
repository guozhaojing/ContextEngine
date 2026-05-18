// =============================================================================
// Models/PromptTrace.cs — execution trace for prompt orchestration
// =============================================================================

namespace Core.Prompting.Models;

public sealed class PromptTrace
{
    public required string TraceId { get; init; }
    public required string Query { get; init; }
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public required IReadOnlyList<TraceStep> Steps { get; init; }
    public int TotalSteps => Steps.Count;
    public long TotalElapsedMs => Steps.Sum(s => s.ElapsedMs);
    public bool HasErrors => Steps.Any(s => s.Status == TraceStepStatus.Error);
}

public sealed class TraceStep
{
    public required string StepId { get; init; }
    public required string Phase { get; init; }
    public required string Description { get; init; }
    public TraceStepStatus Status { get; init; } = TraceStepStatus.Success;
    public IReadOnlyDictionary<string, string> Details { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public long ElapsedMs { get; init; }
}

public enum TraceStepStatus
{
    Success,
    Warning,
    Error,
    Skipped
}

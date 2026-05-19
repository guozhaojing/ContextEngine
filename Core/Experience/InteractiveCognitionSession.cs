// =============================================================================
// Experience/InteractiveCognitionSession.cs — interactive developer investigation
// =============================================================================
// Determinism: session state is tracked deterministically; same sequence → same state.
// Provenance: investigation history records each query + result for audit.
// Replay: InvestigationHistory implements IEquatable for session comparison.
// Grounding: context carry-over preserves evidence chains across follow-up queries.
// =============================================================================

using Core.Cognition;
using Core.Grounding.Confidence;

namespace Core.Experience;

public sealed class InteractiveCognitionSession
{
    private readonly RepositorySession _repoSession;
    private readonly InteractiveSessionOptions _options;
    private readonly List<InvestigationEntry> _history = new();
    private InvestigationContext? _currentContext;

    public InteractiveCognitionSession(
        RepositorySession repoSession,
        InteractiveSessionOptions? options = null)
    {
        _repoSession = repoSession ?? throw new ArgumentNullException(nameof(repoSession));
        _options = options ?? InteractiveSessionOptions.Default;
        SessionId = $"interactive-{DateTime.UtcNow:HHmmss}";
    }

    public string SessionId { get; }
    public IReadOnlyList<InvestigationEntry> History => _history.AsReadOnly();
    public int QueryCount => _history.Count;
    public InvestigationContext? CurrentContext => _currentContext;

    public InteractiveResponse Ask(string question)
    {
        var interpreted = _repoSession.Interpreter!.Interpret(question);

        var contextEnhancedQuery = ApplyContext(interpreted.NormalizedQuery);

        var routing = _repoSession.Router!.Route(interpreted);
        var result = routing.Execute(contextEnhancedQuery);

        var entry = new InvestigationEntry
        {
            EntryId = $"inv-{_history.Count:D5}",
            SequenceIndex = _history.Count,
            Timestamp = DateTime.UtcNow.ToString("O"),
            Question = question,
            RoutedTo = routing.RoutedTo,
            Result = result,
            ContextSnapshot = _currentContext?.Clone(),
        };

        _history.Add(entry);
        UpdateContext(entry);

        var formattedResponse = _repoSession.Formatter!.FormatWithSummary(
            result, question, $"{routing.RoutedTo} ({routing.Confidence})");

        return new InteractiveResponse
        {
            Question = question,
            FormattedResponse = formattedResponse,
            CognitionResult = result,
            RoutedTo = routing.RoutedTo,
            CanFollowUp = result.Explanations.Count > 0 || result.EvidenceCount > 0,
            SuggestedFollowUps = GenerateSuggestedFollowUps(entry, routing.RoutedTo, result),
            QueryNumber = _history.Count,
        };
    }

    public InteractiveResponse FollowUp(string question)
    {
        return Ask(question);
    }

    public InvestigationHistorySnapshot Snapshot()
    {
        return new InvestigationHistorySnapshot
        {
            SessionId = SessionId,
            QueryCount = _history.Count,
            EntryIds = _history.Select(e => e.EntryId).OrderBy(e => e, StringComparer.Ordinal).ToList(),
            ArchivedAt = DateTime.UtcNow.ToString("O"),
        };
    }

    public InvestigationSummary Summarize()
    {
        var architectureCount = _history.Count(e =>
            e.RoutedTo == "ArchitectureExplorer");
        var impactCount = _history.Count(e =>
            e.RoutedTo == "ChangeImpactAnalyzer");
        var debuggingCount = _history.Count(e =>
            e.RoutedTo == "GroundedRootCauseExplorer");
        var capabilityCount = _history.Count(e =>
            e.RoutedTo == "BusinessCapabilityMapper");

        var avgConfidence = _history.Count > 0
            ? _history.Average(e => e.Result.OverallConfidence.GetHashCode()) / 10.0 : 0;

        var topEvidenced = _history
            .OrderByDescending(e => e.Result.EvidenceCount)
            .Take(3)
            .Select(e => e.Question)
            .ToList();

        return new InvestigationSummary
        {
            SessionId = SessionId,
            TotalQuestions = _history.Count,
            ArchitectureQuestions = architectureCount,
            ImpactQuestions = impactCount,
            DebuggingQuestions = debuggingCount,
            CapabilityQuestions = capabilityCount,
            TopEvidencedQuestions = topEvidenced,
        };
    }

    private string ApplyContext(string normalizedQuery)
    {
        if (_currentContext is null) return normalizedQuery;

        if (!string.IsNullOrEmpty(_currentContext.LastTarget))
        {
            if (!normalizedQuery.Contains("it", StringComparison.OrdinalIgnoreCase)
                && !normalizedQuery.Contains("this", StringComparison.OrdinalIgnoreCase))
                return normalizedQuery;

            return normalizedQuery
                .Replace(" it ", $" {_currentContext.LastTarget} ", StringComparison.OrdinalIgnoreCase)
                .Replace(" this ", $" {_currentContext.LastTarget} ", StringComparison.OrdinalIgnoreCase);
        }

        return normalizedQuery;
    }

    private void UpdateContext(InvestigationEntry entry)
    {
        _currentContext ??= new InvestigationContext();

        _currentContext.LastQuery = entry.Question;
        _currentContext.LastEngine = entry.RoutedTo;
        _currentContext.LastResultType = entry.Result.ResultType;

        var firstCitedNode = entry.Result.Citations
            .OrderByDescending(c => c.ConfidenceLevel)
            .FirstOrDefault();

        if (firstCitedNode is not null)
        {
            _currentContext.LastTarget = firstCitedNode.SourceNodeLabel;
            _currentContext.ContextNodeCount = entry.Result.Citations.Count;
        }

        _currentContext.ChainDepth++;
    }

    private IReadOnlyList<string> GenerateSuggestedFollowUps(
        InvestigationEntry entry,
        string routedTo,
        CognitionResult result)
    {
        var suggestions = new List<string>();

        if (entry.Result.Explanations.Count > 0)
        {
            suggestions.Add("能解释得更详细一些吗?");
        }

        var firstCited = result.Citations
            .OrderByDescending(c => c.ConfidenceLevel)
            .FirstOrDefault();

        if (firstCited is not null && !string.IsNullOrEmpty(firstCited.SourceNodeLabel))
        {
            suggestions.Add($"改动 {firstCited.SourceNodeLabel} 会有什么影响?");
            suggestions.Add($"谁依赖 {firstCited.SourceNodeLabel}?");
        }

        switch (routedTo)
        {
            case "ArchitectureExplorer":
                suggestions.Add("集成点有哪些?");
                suggestions.Add("展示依赖关系。");
                break;
            case "ChangeImpactAnalyzer":
                suggestions.Add("这次改动的风险等级是多少?");
                break;
            case "GroundedRootCauseExplorer":
                suggestions.Add("还有哪些组件受影响?");
                suggestions.Add("如何修复这个问题?");
                break;
            case "BusinessCapabilityMapper":
                suggestions.Add("有哪些相关能力?");
                suggestions.Add("展示执行链路。");
                break;
        }

        return suggestions
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .Take(5)
            .ToList();
    }
}

public class InteractiveSessionOptions
{
    public int MaxHistoryEntries { get; init; } = 100;
    public int MaxContextChainDepth { get; init; } = 10;
    public int MaxFollowUpSuggestions { get; init; } = 5;
    public bool EnableContextCarryOver { get; init; } = true;
    public bool AutoSuggestFollowUps { get; init; } = true;

    public static InteractiveSessionOptions Default => new();
}

public sealed class InteractiveResponse
{
    public required string Question { get; init; }
    public required string FormattedResponse { get; init; }
    public required CognitionResult CognitionResult { get; init; }
    public required string RoutedTo { get; init; }
    public bool CanFollowUp { get; init; }
    public required IReadOnlyList<string> SuggestedFollowUps { get; init; }
    public int QueryNumber { get; init; }
}

public sealed class InvestigationEntry : IEquatable<InvestigationEntry>
{
    public required string EntryId { get; init; }
    public int SequenceIndex { get; init; }
    public string Timestamp { get; init; } = "";
    public required string Question { get; init; }
    public required string RoutedTo { get; init; }
    public required CognitionResult Result { get; init; }
    public InvestigationContext? ContextSnapshot { get; init; }

    public bool Equals(InvestigationEntry? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(EntryId, other.EntryId)
            && Result.Equals(other.Result);
    }

    public override bool Equals(object? obj) => obj is InvestigationEntry other && Equals(other);
    public override int GetHashCode() => EntryId.GetHashCode(StringComparison.Ordinal);
}

public sealed class InvestigationContext
{
    public string? LastQuery { get; set; }
    public string? LastEngine { get; set; }
    public CognitionResultType LastResultType { get; set; }
    public string? LastTarget { get; set; }
    public int ContextNodeCount { get; set; }
    public int ChainDepth { get; set; }

    public InvestigationContext Clone()
    {
        return new InvestigationContext
        {
            LastQuery = LastQuery,
            LastEngine = LastEngine,
            LastResultType = LastResultType,
            LastTarget = LastTarget,
            ContextNodeCount = ContextNodeCount,
            ChainDepth = ChainDepth,
        };
    }
}

public sealed class InvestigationHistorySnapshot : IEquatable<InvestigationHistorySnapshot>
{
    public string SessionId { get; init; } = "";
    public string ArchivedAt { get; init; } = "";
    public int QueryCount { get; init; }
    public required IReadOnlyList<string> EntryIds { get; init; }

    public bool Equals(InvestigationHistorySnapshot? other)
    {
        if (other is null) return false;
        if (QueryCount != other.QueryCount) return false;
        if (EntryIds.Count != other.EntryIds.Count) return false;
        for (var i = 0; i < EntryIds.Count; i++)
            if (!StringComparer.Ordinal.Equals(EntryIds[i], other.EntryIds[i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is InvestigationHistorySnapshot other && Equals(other);
    public override int GetHashCode() => QueryCount;
}

public sealed class InvestigationSummary
{
    public string SessionId { get; init; } = "";
    public int TotalQuestions { get; init; }
    public int ArchitectureQuestions { get; init; }
    public int ImpactQuestions { get; init; }
    public int DebuggingQuestions { get; init; }
    public int CapabilityQuestions { get; init; }
    public required IReadOnlyList<string> TopEvidencedQuestions { get; init; }

    public string Format()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"交互会话: {SessionId}");
        sb.AppendLine($"总提问数: {TotalQuestions}");
        sb.AppendLine($"  架构探索: {ArchitectureQuestions}");
        sb.AppendLine($"  影响分析: {ImpactQuestions}");
        sb.AppendLine($"  调试分析: {DebuggingQuestions}");
        sb.AppendLine($"  能力发现: {CapabilityQuestions}");
        return sb.ToString();
    }
}

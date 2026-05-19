// =============================================================================
// Experience/DeveloperQueryInterpreter.cs — interprets natural developer questions
// =============================================================================
// Determinism: intent classification uses keyword matching, not ML.
// Provenance: each interpretation captures the extracted entities and intent.
// Replay: InterpretedQuery implements IEquatable for regression comparison.
// Grounding: interpretation never hallucinates entities; only extracts from query text.
// =============================================================================

using Core.Cognition;
using Core.Evaluation.Cognition;

namespace Core.Experience;

public sealed class DeveloperQueryInterpreter
{
    private readonly QueryInterpreterOptions _options;

    public DeveloperQueryInterpreter(QueryInterpreterOptions? options = null)
    {
        _options = options ?? QueryInterpreterOptions.Default;
    }

    public InterpretedQuery Interpret(string rawQuery)
    {
        var normalized = Normalize(rawQuery);
        var intent = ClassifyIntent(normalized);
        var entities = ExtractEntities(normalized);
        var confidence = DetermineInterpretationConfidence(normalized, intent, entities);
        var taskType = MapIntentToTaskType(intent);

        return new InterpretedQuery
        {
            RawQuery = rawQuery,
            NormalizedQuery = normalized,
            Intent = intent,
            Entities = entities,
            TaskType = taskType,
            InterpretationConfidence = confidence,
        };
    }

    private static string Normalize(string query)
    {
        return query.Trim()
            .Replace("  ", " ")
            .Replace("?", "")
            .Replace(",", " ")
            .Replace("'", "")
            .Replace("\"", "");
    }

    private DeveloperIntent ClassifyIntent(string normalized)
    {
        var lower = normalized.ToLowerInvariant();

        if (_options.ArchitecturePatterns.Any(p => lower.Contains(p, StringComparison.Ordinal)))
            return DeveloperIntent.ExplainArchitecture;

        if (_options.ImpactPatterns.Any(p => lower.Contains(p, StringComparison.Ordinal)))
            return DeveloperIntent.AnalyzeImpact;

        if (_options.DebugPatterns.Any(p => lower.Contains(p, StringComparison.Ordinal)))
            return DeveloperIntent.DebugIssue;

        if (_options.CapabilityPatterns.Any(p => lower.Contains(p, StringComparison.Ordinal)))
            return DeveloperIntent.MapCapabilities;

        return DeveloperIntent.GeneralQuestion;
    }

    private List<string> ExtractEntities(string normalized)
    {
        var entities = new List<string>();
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words.Take(10))
        {
            if (char.IsUpper(word[0]) && word.Length > 2)
                entities.Add(word);

            if (word.Contains('.', StringComparison.Ordinal) && word.Length > 3)
                entities.Add(word);
        }

        return entities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();
    }

    private InterpretationConfidence DetermineInterpretationConfidence(
        string normalized,
        DeveloperIntent intent,
        List<string> entities)
    {
        if (intent != DeveloperIntent.GeneralQuestion && entities.Count > 0)
            return InterpretationConfidence.High;

        if (intent != DeveloperIntent.GeneralQuestion)
            return InterpretationConfidence.Medium;

        if (entities.Count > 0)
            return InterpretationConfidence.Medium;

        return InterpretationConfidence.Low;
    }

    private static CognitionTaskType MapIntentToTaskType(DeveloperIntent intent)
        => intent switch
        {
            DeveloperIntent.ExplainArchitecture => CognitionTaskType.ArchitectureExplanation,
            DeveloperIntent.AnalyzeImpact => CognitionTaskType.ImpactAnalysis,
            DeveloperIntent.DebugIssue => CognitionTaskType.RootCauseAnalysis,
            DeveloperIntent.MapCapabilities => CognitionTaskType.CapabilityMapping,
            _ => CognitionTaskType.ArchitectureExplanation,
        };
}

public enum DeveloperIntent
{
    GeneralQuestion = 0,
    ExplainArchitecture = 1,
    AnalyzeImpact = 2,
    DebugIssue = 3,
    MapCapabilities = 4,
}

public enum InterpretationConfidence
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public sealed class InterpretedQuery : IEquatable<InterpretedQuery>
{
    public required string RawQuery { get; init; }
    public required string NormalizedQuery { get; init; }
    public DeveloperIntent Intent { get; init; }
    public required IReadOnlyList<string> Entities { get; init; }
    public CognitionTaskType TaskType { get; init; }
    public InterpretationConfidence InterpretationConfidence { get; init; }

    public bool Equals(InterpretedQuery? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(NormalizedQuery, other.NormalizedQuery)
            && Intent == other.Intent;
    }

    public override bool Equals(object? obj) => obj is InterpretedQuery other && Equals(other);
    public override int GetHashCode() => NormalizedQuery.GetHashCode(StringComparison.Ordinal);
}

public class QueryInterpreterOptions
{
    public IReadOnlyList<string> ArchitecturePatterns { get; init; } = new[]
    {
        "architecture", "structure", "subsystem", "layer", "dependency",
        "how is organized", "how is structured", "explain the system",
        "describe the architecture",
    };

    public IReadOnlyList<string> ImpactPatterns { get; init; } = new[]
    {
        "what breaks", "impact of", "who depends", "who calls",
        "what depends on", "what would break", "affected by",
        "consequences of changing", "what is affected",
    };

    public IReadOnlyList<string> DebugPatterns { get; init; } = new[]
    {
        "why does", "why is", "why are", "why isn't", "why doesn't",
        "debug", "fail", "failing", "broken", "error", "exception",
        "timeout", "retry", "crash",
    };

    public IReadOnlyList<string> CapabilityPatterns { get; init; } = new[]
    {
        "where is", "how does", "implemented", "implementation",
        "how do i", "how to", "how are", "how is handled",
        "what handles", "who handles", "where does",
    };

    public static QueryInterpreterOptions Default => new();
}

// =============================================================================
// SemanticDoc/MethodSemanticDoc.cs — method semantic document + reverse index
// =============================================================================
// Purpose: Capture extracted knowledge per method for embedding and retrieval.
// Principle: structural summary, no raw code in EnhancedText.
// =============================================================================

namespace Core.Cognition.SemanticDoc;

public sealed class MethodSemanticDoc
{
    public required string MethodId { get; init; }
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
    public required string Namespace { get; init; }
    public required string ProjectPath { get; init; }
    public required string SourceFile { get; init; }

    // From graph
    public required IReadOnlyList<string> CalledMethods { get; init; }
    public required IReadOnlyList<string> CallerMethods { get; init; }

    // Extracted from method body
    public required IReadOnlyList<string> SqlTables { get; init; }
    public required IReadOnlyList<string> HttpUrls { get; init; }
    public required IReadOnlyList<string> ExceptionTypes { get; init; }
    public required IReadOnlyList<string> DtoTypes { get; init; }
    public required IReadOnlyList<string> FilePaths { get; init; }
    public required IReadOnlyList<string> ConfigKeys { get; init; }

    // Behavior summary (7E — CodeSummarizer fills this)
    public string BehaviorSummary { get; set; } = "";

    // For embedding
    public string ContentHash { get; init; } = "";
    public string EnhancedText => BuildEnhancedText();

    private string BuildEnhancedText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Method:{MethodName}");
        sb.AppendLine($"Class:{ClassName}");
        sb.AppendLine($"Namespace:{Namespace}");

        if (CalledMethods.Count > 0)
            sb.AppendLine($"Calls:{string.Join(',', CalledMethods.Take(10))}");
        if (SqlTables.Count > 0)
            sb.AppendLine($"SQL:{string.Join(',', SqlTables)}");
        if (HttpUrls.Count > 0)
            sb.AppendLine($"HTTP:{string.Join(',', HttpUrls)}");
        if (ExceptionTypes.Count > 0)
            sb.AppendLine($"Exceptions:{string.Join(',', ExceptionTypes)}");
        if (DtoTypes.Count > 0)
            sb.AppendLine($"DTO:{string.Join(',', DtoTypes)}");
        if (FilePaths.Count > 0)
            sb.AppendLine($"Files:{string.Join(',', FilePaths)}");
        if (ConfigKeys.Count > 0)
            sb.AppendLine($"Config:{string.Join(',', ConfigKeys)}");
        if (!string.IsNullOrEmpty(BehaviorSummary))
            sb.AppendLine($"Behavior:{BehaviorSummary}");

        return sb.ToString();
    }
}

// ═══════════════════════════════════════════════════════════════
// Reverse Index Entry (key → methods)
// ═══════════════════════════════════════════════════════════════

public sealed class ReverseIndexEntry
{
    public required string MethodId { get; init; }
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
    public required string ProjectName { get; init; }

    public string DisplayLabel => $"{ClassName}.{MethodName} [{ProjectName}]";
}

public sealed class ReverseIndex
{
    public Dictionary<string, List<ReverseIndexEntry>> TableToMethods { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<ReverseIndexEntry>> HttpUrlToMethods { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<ReverseIndexEntry>> ExceptionToMethods { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<ReverseIndexEntry>> ConfigKeyToMethods { get; } = new(StringComparer.Ordinal);

    public void Add(string category, string key, ReverseIndexEntry entry)
    {
        var dict = category switch
        {
            "table" => TableToMethods,
            "http" => HttpUrlToMethods,
            "exception" => ExceptionToMethods,
            "config" => ConfigKeyToMethods,
            _ => null,
        };
        if (dict is null) return;

        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<ReverseIndexEntry>();
            dict[key] = list;
        }
        if (!list.Any(e => e.MethodId == entry.MethodId))
            list.Add(entry);
    }
}

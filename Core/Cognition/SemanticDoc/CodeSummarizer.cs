// =============================================================================
// SemanticDoc/CodeSummarizer.cs — context compression (7E)
// =============================================================================
// Purpose: Generate structured behavior summaries for each method.
//          No raw code — only "what this method does" in structured form.
//          Inject into LLM context to prevent token explosion.
// =============================================================================

namespace Core.Cognition.SemanticDoc;

public sealed class CodeSummarizer
{
    private readonly SummarizerOptions _options;

    public CodeSummarizer(SummarizerOptions? options = null)
    {
        _options = options ?? SummarizerOptions.Default;
    }

    public void SummarizeAll(SemanticDocResult result)
    {
        foreach (var doc in result.Docs)
        {
            doc.BehaviorSummary = SummarizeOne(doc);
        }
    }

    private string SummarizeOne(MethodSemanticDoc doc)
    {
        var parts = new List<string>();

        // What it does (inferred from method name + callees)
        var action = InferAction(doc);
        if (!string.IsNullOrEmpty(action))
            parts.Add(action);

        // Key data access
        if (doc.SqlTables.Count > 0)
            parts.Add($"accesses:{string.Join(',', doc.SqlTables.Take(3))}");
        if (doc.HttpUrls.Count > 0)
            parts.Add($"endpoint:{doc.HttpUrls[0]}");

        // Key dependencies
        if (doc.CalledMethods.Count > 0)
            parts.Add($"calls:{string.Join(',', doc.CalledMethods.Take(3))}");

        // Exception handling
        if (doc.ExceptionTypes.Count > 0)
        {
            parts.Add(doc.ExceptionTypes.Any(e => e.Contains("catch", StringComparison.OrdinalIgnoreCase))
                ? "handles-errors" : $"throws:{string.Join(',', doc.ExceptionTypes.Take(2))}");
        }

        // DTO usage
        if (doc.DtoTypes.Count > 0)
            parts.Add($"uses:{string.Join(',', doc.DtoTypes.Take(2))}");

        // Config aware
        if (doc.ConfigKeys.Count > 0)
            parts.Add("config-aware");

        // Entry point marker
        if (doc.HttpUrls.Count > 0)
            parts.Add("api-entrypoint");

        return string.Join("; ", parts);
    }

    private static string InferAction(MethodSemanticDoc doc)
    {
        var name = doc.MethodName;

        if (name.StartsWith("Get", StringComparison.Ordinal) || name.StartsWith("Find", StringComparison.Ordinal) || name.StartsWith("Query", StringComparison.Ordinal))
            return "query";
        if (name.StartsWith("Save", StringComparison.Ordinal) || name.StartsWith("Add", StringComparison.Ordinal) || name.StartsWith("Insert", StringComparison.Ordinal) || name.StartsWith("Create", StringComparison.Ordinal))
            return "create";
        if (name.StartsWith("Update", StringComparison.Ordinal) || name.StartsWith("Edit", StringComparison.Ordinal) || name.StartsWith("Modify", StringComparison.Ordinal))
            return "update";
        if (name.StartsWith("Delete", StringComparison.Ordinal) || name.StartsWith("Remove", StringComparison.Ordinal))
            return "delete";
        if (name.Contains("Copy", StringComparison.Ordinal) || name.Contains("Clone", StringComparison.Ordinal))
            return "clone";
        if (name.Contains("Validate", StringComparison.Ordinal) || name.Contains("Check", StringComparison.Ordinal) || name.Contains("Verify", StringComparison.Ordinal))
            return "validate";
        if (name.Contains("Notify", StringComparison.Ordinal) || name.Contains("Send", StringComparison.Ordinal) || name.Contains("Publish", StringComparison.Ordinal))
            return "notify";
        if (name.Contains("Process", StringComparison.Ordinal) || name.Contains("Handle", StringComparison.Ordinal) || name.Contains("Execute", StringComparison.Ordinal))
            return "process";
        if (name.Contains("Sync", StringComparison.Ordinal) || name.Contains("Import", StringComparison.Ordinal) || name.Contains("Export", StringComparison.Ordinal))
            return "sync";
        if (name.Contains("Search", StringComparison.Ordinal) || name.Contains("List", StringComparison.Ordinal) || name.Contains("Load", StringComparison.Ordinal))
            return "query";

        return "";
    }
}

public class SummarizerOptions
{
    public int MaxSummaryLength { get; init; } = 200;
    public static SummarizerOptions Default => new();
}

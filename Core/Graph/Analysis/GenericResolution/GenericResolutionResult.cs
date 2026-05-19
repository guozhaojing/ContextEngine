// =============================================================================
// GenericResolution/GenericResolutionResult.cs — 泛型解析结果收集 + Origin Trace
// =============================================================================

namespace Core.Graph.Analysis.GenericResolution;

public sealed class GenericResolutionResult
{
    public string AnalyzerName { get; set; } = "generic-resolution";
    public string ScanRoot { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int ClassesScanned { get; set; }
    public int RepositoryClassesFound { get; set; }
    public int TotalInvocationsResolved { get; set; }
    public int TotalEntitiesDiscovered { get; set; }
    public int TotalTablesDiscovered { get; set; }
    public List<GenericResolutionEntry> Resolutions { get; set; } = new();
    public List<string> DiscoveredEntities { get; set; } = new();
    public List<string> DiscoveredTables { get; set; } = new();
    public Dictionary<string, List<string>> EntityClassToTableMap { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ResolutionByMethod { get; set; } = new(StringComparer.Ordinal);
    public List<string> Errors { get; set; } = new();
    public List<GenericDiagnostic> Diagnostics { get; set; } = new();
    public int EdgesProduced { get; set; }
    public int FactsProduced { get; set; }
    public int AnnotationsProduced { get; set; }

    public void Record(
        string methodId,
        string entityClass,
        string entityNamespace,
        string table,
        GenericResolutionConfidence confidence,
        string resolutionMethod,
        string viaClass,
        string? sourceFile = null)
    {
        Resolutions.Add(new GenericResolutionEntry
        {
            MethodId = methodId,
            EntityClass = entityClass,
            EntityNamespace = entityNamespace,
            Table = table,
            Confidence = confidence,
            ResolutionMethod = resolutionMethod,
            ViaClass = viaClass,
            SourceFile = sourceFile,
            OriginTrace = $"{sourceFile ?? "?"} → {viaClass} → {resolutionMethod}"
        });

        if (confidence >= GenericResolutionConfidence.Medium)
        {
            if (!DiscoveredEntities.Contains(entityClass, StringComparer.Ordinal))
                DiscoveredEntities.Add(entityClass);
            if (!DiscoveredTables.Contains(table, StringComparer.Ordinal))
                DiscoveredTables.Add(table);
            if (!EntityClassToTableMap.ContainsKey(entityClass))
                EntityClassToTableMap[entityClass] = new List<string>();
            if (!EntityClassToTableMap[entityClass].Contains(table))
                EntityClassToTableMap[entityClass].Add(table);
        }
        ResolutionByMethod[resolutionMethod] =
            ResolutionByMethod.GetValueOrDefault(resolutionMethod, 0) + 1;
    }

    public void RecordEntityBinding(string entityClass, string sourceFile, string bindingPath)
    {
        if (!DiscoveredEntities.Contains(entityClass, StringComparer.Ordinal))
            DiscoveredEntities.Add(entityClass);
        if (!DiscoveredTables.Contains(entityClass + "s", StringComparer.Ordinal))
            DiscoveredTables.Add(entityClass + "s");
        if (!EntityClassToTableMap.ContainsKey(entityClass))
            EntityClassToTableMap[entityClass] = new List<string>();
        var tbl = entityClass + "s";
        if (!EntityClassToTableMap[entityClass].Contains(tbl))
            EntityClassToTableMap[entityClass].Add(tbl);
    }
}

public sealed class GenericResolutionEntry
{
    public string MethodId { get; set; } = "";
    public string EntityClass { get; set; } = "";
    public string EntityNamespace { get; set; } = "";
    public string Table { get; set; } = "";
    public GenericResolutionConfidence Confidence { get; set; }
    public string ResolutionMethod { get; set; } = "";
    public string ViaClass { get; set; } = "";
    public string? SourceFile { get; set; }
    public string OriginTrace { get; set; } = "";
}

public enum DiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed class GenericDiagnostic
{
    public DiagnosticSeverity Severity { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? EntityClass { get; set; }
    public string? ContextClass { get; set; }
    public string? SourceFile { get; set; }
}

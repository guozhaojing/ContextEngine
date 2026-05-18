// =============================================================================
// GenericResolution/GenericEntityAccessFact.cs — 泛型 Entity Access 事实
// =============================================================================
// 记录泛型解析链路：method → (generic chain) → entity → table
// =============================================================================

namespace Core.Graph.Analysis.GenericResolution;

public sealed class GenericEntityAccessFact
{
    public string MethodId { get; set; } = "";

    public string ResolvedEntityClass { get; set; } = "";

    public string ResolvedEntityNamespace { get; set; } = "";

    public string ResolvedTable { get; set; } = "";

    public string ViaClass { get; set; } = "";

    public string ViaBaseType { get; set; } = "";

    public string ResolutionMethod { get; set; } = "";

    public GenericResolutionConfidence Confidence { get; set; } = GenericResolutionConfidence.None;

    public string? SourceFile { get; set; }

    public string? CallerMethodName { get; set; }

    public Dictionary<string, string> ToFactData(string apiName = "generic")
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api"] = apiName,
            ["entityClass"] = ResolvedEntityClass,
            ["entityNamespace"] = ResolvedEntityNamespace,
            ["table"] = ResolvedTable,
            ["confidence"] = Confidence.ToLowerString(),
            ["viaClass"] = ViaClass,
            ["viaBaseType"] = ViaBaseType,
            ["resolution"] = ResolutionMethod
        };
    }

    public string ToEdgeLabel() =>
        $"generic:{ResolvedEntityClass} → {ResolvedTable} (via {ViaClass})";

    public static GenericEntityAccessFact Empty(string methodId) => new()
    {
        MethodId = methodId,
        Confidence = GenericResolutionConfidence.None
    };
}

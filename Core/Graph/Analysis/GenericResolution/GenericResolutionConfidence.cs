// =============================================================================
// GenericResolution/GenericResolutionConfidence.cs — 泛型解析置信度
// =============================================================================
// 扩展标准 ResolutionConfidence，加入泛型解析专有级别。
// =============================================================================

namespace Core.Graph.Analysis.GenericResolution;

public enum GenericResolutionConfidence
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Exact = 4
}

public static class GenericConfidenceExtensions
{
    public static ResolutionConfidence ToStandardConfidence(this GenericResolutionConfidence g)
    {
        return g switch
        {
            GenericResolutionConfidence.Exact => ResolutionConfidence.Exact,
            GenericResolutionConfidence.High => ResolutionConfidence.High,
            GenericResolutionConfidence.Medium => ResolutionConfidence.Medium,
            GenericResolutionConfidence.Low => ResolutionConfidence.Low,
            _ => ResolutionConfidence.Low
        };
    }

    public static string ToLowerString(this GenericResolutionConfidence g) =>
        g.ToString().ToLowerInvariant();
}

// =============================================================================
// GenericResolution/DaoCallSiteResolver.cs — BLL→DAO 调用 Entity 传播 (Strict Mode)
// =============================================================================
// 【Strict】只在 BLL 方法体内检测到对 DAO 字段的实际调用时才绑定 Entity。
//   禁止：无调用时自动绑定、跨方法链传播、低置信度传播。
// =============================================================================

using System.Text.RegularExpressions;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class DaoCallSiteResolver
{
    private readonly EntityClassRegistry _registry;

    public DaoCallSiteResolver(EntityClassRegistry registry)
    {
        _registry = registry;
    }

    public List<DaoCallSite> Resolve(
        string methodContent,
        Dictionary<string, DaoFieldMatch> daoFields,
        string bllClassName)
    {
        var results = new List<DaoCallSite>();

        if (daoFields.Count == 0) return results;

        foreach (var (_, fieldMatch) in daoFields)
        {
            var entityName = fieldMatch.EntityName;

            if (entityName is null)
            {
                var binding = _registry.GetBindingForClass(fieldMatch.DaoClassName);
                if (binding is not null)
                    entityName = binding.EntityType;
            }

            if (entityName is null) continue;

            var calls = FindCallsOnField(methodContent, fieldMatch.FieldName);

            foreach (var call in calls)
            {
                results.Add(new DaoCallSite
                {
                    BllClassName = bllClassName,
                    DaoFieldName = fieldMatch.FieldName,
                    DaoClassName = fieldMatch.DaoClassName,
                    EntityName = entityName,
                    CalledMethod = call,
                    Confidence = fieldMatch.Confidence
                });
            }
        }

        return results;
    }

    private static List<string> FindCallsOnField(string methodContent, string fieldName)
    {
        var calls = new List<string>();

        var escaped = Regex.Escape(fieldName);
        var pattern = $@"\b{escaped}\.(\w+)\s*\(";
        var matches = Regex.Matches(methodContent, pattern);

        foreach (Match match in matches)
        {
            var methodName = match.Groups[1].Value;
            if (!calls.Contains(methodName))
                calls.Add(methodName);
        }

        var thisPattern = $@"this\.{escaped}\.(\w+)\s*\(";
        var thisMatches = Regex.Matches(methodContent, thisPattern);

        foreach (Match match in thisMatches)
        {
            var methodName = match.Groups[1].Value;
            if (!calls.Contains(methodName))
                calls.Add(methodName);
        }

        return calls;
    }
}

public sealed class DaoCallSite
{
    public string BllClassName { get; set; } = "";
    public string DaoFieldName { get; set; } = "";
    public string DaoClassName { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string CalledMethod { get; set; } = "";
    public GenericResolutionConfidence Confidence { get; set; }
}

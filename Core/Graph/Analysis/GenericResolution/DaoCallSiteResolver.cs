// =============================================================================
// GenericResolution/DaoCallSiteResolver.cs — BLL 方法 → DAO 方法的 Entity 传播
// =============================================================================
// 在 BLL 方法体内检测对 DAO 字段的方法调用：
//   return dao.GetListByWhere(where);    → dao 字段 → Entity
//   return _repository.FindAll();         → _repository 字段 → Entity
//   var list = m_dao.Search(query);       → m_dao 字段 → Entity
//
// 原理：
//   1. 找到 BLL 类持有的 DAO 字段（通过 DaoFieldDetector）
//   2. 在 BLL 方法体内查找对该字段的方法调用
//   3. 从 DAO 字段类型（泛型）推导触达的 Entity
//   4. 将该 Entity 绑定回传给 BLL 方法
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
                entityName = _registry.GetEntityForClass(fieldMatch.DaoClassName);
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

            if (calls.Count == 0)
            {
                results.Add(new DaoCallSite
                {
                    BllClassName = bllClassName,
                    DaoFieldName = fieldMatch.FieldName,
                    DaoClassName = fieldMatch.DaoClassName,
                    EntityName = entityName,
                    CalledMethod = "*",
                    Confidence = GenericResolutionConfidence.Medium
                });
            }
        }

        return results;
    }

    private static List<string> FindCallsOnField(string methodContent, string fieldName)
    {
        var calls = new List<string>();

        var pattern = $@"{Regex.Escape(fieldName)}\.(\w+)\s*\(";
        var matches = Regex.Matches(methodContent, pattern);

        foreach (Match match in matches)
        {
            var methodName = match.Groups[1].Value;
            if (!calls.Contains(methodName))
                calls.Add(methodName);
        }

        var thisPattern = $@"this\.{Regex.Escape(fieldName)}\.(\w+)\s*\(";
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

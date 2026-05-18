using Core.Graph.Identity;
using Core.Models;
using Core.Semantics;

namespace Core.Graph.Building;

/// <summary>
/// 扫描结果的方法索引，供图构建阶段将语义解析结果映射为稳定 MethodId。
/// </summary>
internal sealed class MethodRegistry
{
    private readonly Dictionary<string, string> _byProjectAndQualified = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byQualified = new(StringComparer.Ordinal);

    public MethodRegistry(IEnumerable<CodeUnit> units)
    {
        foreach (var unit in units)
        {
            var methodId = MethodIdBuilder.FromCodeUnit(unit).Value;
            var qualified = MethodIdBuilder.BuildQualifiedName(unit.Namespace, unit.ClassName, unit.MethodName);
            var projectKey = $"{MethodIdBuilder.NormalizeProjectPath(unit.ProjectPath)}|{qualified}";

            _byProjectAndQualified[projectKey] = methodId;
            AddQualified(qualified, methodId);
        }
    }

    public bool TryResolve(ResolvedMethodInfo target, string sourceProjectPath, out string methodId)
    {
        methodId = "";

        if (target.IsExternal)
        {
            methodId = MethodIdBuilder.ForExternal(target).Value;
            return true;
        }

        var qualified = MethodIdBuilder.BuildQualifiedName(target.Namespace, target.ClassName, target.MethodName);
        var projectKey = $"{MethodIdBuilder.NormalizeProjectPath(sourceProjectPath)}|{qualified}";

        if (_byProjectAndQualified.TryGetValue(projectKey, out methodId!))
            return true;

        if (_byQualified.TryGetValue(qualified, out var candidates) && candidates.Count == 1)
        {
            methodId = candidates[0];
            return true;
        }

        return false;
    }

    public bool Contains(string methodId) =>
        _byProjectAndQualified.ContainsValue(methodId)
        || _byQualified.Values.Any(ids => ids.Contains(methodId, StringComparer.Ordinal));

    private void AddQualified(string qualified, string methodId)
    {
        if (!_byQualified.TryGetValue(qualified, out var list))
        {
            list = [];
            _byQualified[qualified] = list;
        }

        if (!list.Contains(methodId, StringComparer.Ordinal))
            list.Add(methodId);
    }
}

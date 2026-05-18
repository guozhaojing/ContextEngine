// =============================================================================
// Graph/Building/MethodRegistry.cs — 解决方案内方法的索引表
// =============================================================================
// 建图时把 ResolvedMethodInfo 映射到 MethodId。
// 先按「项目+限定名」精确匹配，再按「限定名唯一」回退。
// =============================================================================

using Core.Graph.Identity;
using Core.Models;
using Core.Semantics;

namespace Core.Graph.Building;

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

        // 全解决方案中限定名唯一时才回退（避免重名方法连错边）
        if (_byQualified.TryGetValue(qualified, out var candidates) && candidates.Count == 1)
        {
            methodId = candidates[0];
            return true;
        }

        return false;
    }

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

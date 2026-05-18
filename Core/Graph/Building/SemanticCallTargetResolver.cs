using Core.Semantics;

namespace Core.Graph.Building;

/// <summary>
/// 将语义解析结果映射为图中的目标 MethodId（仅用于构建阶段，不含查询逻辑）。
/// </summary>
internal sealed class SemanticCallTargetResolver
{
    private readonly MethodRegistry _registry;

    public SemanticCallTargetResolver(MethodRegistry registry)
    {
        _registry = registry;
    }

    public bool TryResolveTargetId(
        ResolvedMethodInfo target,
        string sourceProjectPath,
        out string targetMethodId)
    {
        return _registry.TryResolve(target, sourceProjectPath, out targetMethodId);
    }
}

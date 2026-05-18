// =============================================================================
// Graph/Building/SemanticCallTargetResolver.cs
// =============================================================================
// 将扫描阶段的 ResolvedMethodInfo 转为图中的目标 MethodId。
// 仅用于 CodeGraphBuilder，不含查询逻辑。
// =============================================================================

using Core.Semantics;

namespace Core.Graph.Building;

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

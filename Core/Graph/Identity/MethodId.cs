// =============================================================================
// Graph/Identity/MethodId.cs — 方法的稳定唯一标识（值对象）
// =============================================================================

namespace Core.Graph.Identity;

/// <summary>
/// 稳定的方法标识，用于图节点主键与增量扫描对齐。
/// </summary>
public readonly record struct MethodId(string Value)
{
    public override string ToString() => Value;

    public static implicit operator string(MethodId id) => id.Value;
}

namespace Core.Graph.Identity;

/// <summary>
/// 稳定的方法标识，用于增量扫描与图节点主键。
/// </summary>
public readonly record struct MethodId(string Value)
{
    public override string ToString() => Value;

    public static implicit operator string(MethodId id) => id.Value;
}

// =============================================================================
// Semantics/ResolvedMethodInfo.cs — 一次调用表达式的语义解析结果
// =============================================================================
// 仅描述“调用了哪个方法”，不包含图、查询、存储逻辑。
// =============================================================================

namespace Core.Semantics;

/// <summary>
/// Roslyn 解析 InvocationExpressionSyntax 后得到的目标方法信息。
/// </summary>
public class ResolvedMethodInfo
{
    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    /// <summary>
    /// true = 目标不在当前解决方案源码中（BCL、NuGet 等）或无法解析。
    /// </summary>
    public bool IsExternal { get; set; }
}

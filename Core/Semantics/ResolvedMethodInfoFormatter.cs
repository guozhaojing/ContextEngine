// =============================================================================
// Semantics/ResolvedMethodInfoFormatter.cs — 将解析结果格式化为可读字符串
// =============================================================================

namespace Core.Semantics;

public static class ResolvedMethodInfoFormatter
{
    /// <summary>例如：MyApp.Services.AuditService.Audit</summary>
    public static string ToQualifiedName(ResolvedMethodInfo info)
    {
        if (string.IsNullOrEmpty(info.ClassName))
            return info.MethodName;

        if (string.IsNullOrEmpty(info.Namespace))
            return $"{info.ClassName}.{info.MethodName}";

        return $"{info.Namespace}.{info.ClassName}.{info.MethodName}";
    }
}

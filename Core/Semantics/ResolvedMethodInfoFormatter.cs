// =============================================================================
// Semantics/ResolvedMethodInfoFormatter.cs — 将解析结果格式化为可读字符串
// =============================================================================

namespace Core.Semantics;

public static class ResolvedMethodInfoFormatter
{
    /// <summary>例如：MyApp.Services.AuditService.Audit 或 ...Audit(int, string)</summary>
    public static string ToQualifiedName(ResolvedMethodInfo info)
    {
        string baseName;
        if (string.IsNullOrEmpty(info.ClassName))
            baseName = info.MethodName;
        else if (string.IsNullOrEmpty(info.Namespace))
            baseName = $"{info.ClassName}.{info.MethodName}";
        else
            baseName = $"{info.Namespace}.{info.ClassName}.{info.MethodName}";

        if (info.ParameterTypes.Count > 0)
            return $"{baseName}({string.Join(", ", info.ParameterTypes)})";

        return baseName;
    }
}

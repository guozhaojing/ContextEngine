namespace Core.Semantics;

public static class ResolvedMethodInfoFormatter
{
    public static string ToQualifiedName(ResolvedMethodInfo info)
    {
        if (string.IsNullOrEmpty(info.ClassName))
            return info.MethodName;

        if (string.IsNullOrEmpty(info.Namespace))
            return $"{info.ClassName}.{info.MethodName}";

        return $"{info.Namespace}.{info.ClassName}.{info.MethodName}";
    }
}

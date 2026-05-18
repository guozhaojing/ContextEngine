// =============================================================================
// Graph/Identity/MethodIdBuilder.cs — 生成稳定的 MethodId
// =============================================================================
// 内部：method:{项目路径}::{命名空间.类.方法}
// 外部：ext::{限定名}
// 不依赖源文件路径，便于项目移动后 Id 仍稳定（只要 csproj 相对路径不变）。
// =============================================================================

using Core.Models;
using Core.Semantics;

namespace Core.Graph.Identity;

public static class MethodIdBuilder
{
    private const string InternalPrefix = "method";
    private const string ExternalPrefix = "ext";

    public static MethodId FromCodeUnit(CodeUnit unit) =>
        FromMethod(unit.ProjectPath, unit.Namespace, unit.ClassName, unit.MethodName);

    public static MethodId FromMethod(
        string projectPath,
        string namespaceName,
        string className,
        string methodName)
    {
        var normalizedProject = NormalizeProjectPath(projectPath);
        var qualifiedName = BuildQualifiedName(namespaceName, className, methodName);
        return new MethodId($"{InternalPrefix}:{normalizedProject}::{qualifiedName}");
    }

    public static MethodId FromResolvedMethod(ResolvedMethodInfo resolved, string? sourceProjectPath = null)
    {
        if (resolved.IsExternal)
            return ForExternal(resolved);

        if (!string.IsNullOrEmpty(sourceProjectPath))
            return FromMethod(sourceProjectPath, resolved.Namespace, resolved.ClassName, resolved.MethodName);

        var qualifiedName = BuildQualifiedName(resolved.Namespace, resolved.ClassName, resolved.MethodName);
        return new MethodId($"{InternalPrefix}::{qualifiedName}");
    }

    public static MethodId ForExternal(ResolvedMethodInfo resolved)
    {
        var qualifiedName = BuildQualifiedName(resolved.Namespace, resolved.ClassName, resolved.MethodName);
        if (string.IsNullOrEmpty(qualifiedName) || qualifiedName == ".")
            qualifiedName = resolved.MethodName;

        return new MethodId($"{ExternalPrefix}::{qualifiedName}");
    }

    public static string BuildQualifiedName(string namespaceName, string className, string methodName)
    {
        if (string.IsNullOrEmpty(namespaceName))
            return $"{className}.{methodName}";

        return $"{namespaceName}.{className}.{methodName}";
    }

    public static string NormalizeProjectPath(string projectPath) =>
        projectPath.Replace('\\', '/').Trim().ToLowerInvariant();
}

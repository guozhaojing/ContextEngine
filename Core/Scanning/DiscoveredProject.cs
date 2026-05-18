// =============================================================================
// Scanning/DiscoveredProject.cs — 发现的一个 .NET 项目
// =============================================================================

namespace Core.Scanning;

/// <summary>
/// 从 .sln 或目录扫描得到的单个 csproj 项目信息。
/// </summary>
/// <param name="Name">项目显示名（来自 sln 或 csproj 文件名）</param>
/// <param name="ProjectFilePath">.csproj 绝对路径</param>
/// <param name="ProjectDirectory">项目目录（用于枚举 .cs 文件）</param>
public sealed record DiscoveredProject(
    string Name,
    string ProjectFilePath,
    string ProjectDirectory);

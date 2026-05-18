// =============================================================================
// Models/CodeUnit.cs — 扫描阶段的核心数据结构
// =============================================================================
// 一个 CodeUnit = 解决方案中的一个方法（含源码、调用列表）。
// 后续建图时每个 CodeUnit 对应图中的一个节点。
// =============================================================================

using Core.Semantics;

namespace Core.Models;

/// <summary>
/// 从源码中提取的一个方法单元。
/// </summary>
public class CodeUnit
{
    /// <summary>稳定的方法 Id（见 MethodIdBuilder），用于图节点主键。</summary>
    public string Id { get; set; } = "";

    /// <summary>源文件绝对路径。</summary>
    public string FilePath { get; set; } = "";

    /// <summary>相对于扫描根目录的文件路径。</summary>
    public string RelativeFilePath { get; set; } = "";

    /// <summary>所属项目名称（.csproj 文件名不含扩展名）。</summary>
    public string ProjectName { get; set; } = "";

    /// <summary>所属项目 .csproj 相对于扫描根的路径。</summary>
    public string ProjectPath { get; set; } = "";

    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    /// <summary>参数类型列表，如 ["int", "string"]，用于区分重载。</summary>
    public List<string> ParameterTypes { get; set; } = new();

    /// <summary>方法体源码（块体或表达式体）。</summary>
    public string Content { get; set; } = "";

    /// <summary>调用的限定名列表（由 ResolvedCalls 格式化，兼容导出）。</summary>
    public List<string> Calls { get; set; } = new();

    /// <summary>语义解析后的调用目标（Roslyn GetSymbolInfo 结果）。</summary>
    public List<ResolvedMethodInfo> ResolvedCalls { get; set; } = new();
}

// =============================================================================
// Graph/Analysis/GraphFact.cs — 结构化分析事实（合并到 CodeGraph.Facts）
// =============================================================================

namespace Core.Graph.Analysis;

public sealed class GraphFact
{
    public string Analyzer { get; set; } = "";

    /// <summary>关联主体 Id，通常是 methodId。</summary>
    public string SubjectId { get; set; } = "";

    public string SubjectKind { get; set; } = GraphSubjectKinds.Method;

    public string FactType { get; set; } = "";

    /// <summary>来源文件（增量扫描时用于按文件替换该分析器的旧事实）。</summary>
    public string? SourceFile { get; set; }

    public Dictionary<string, string> Data { get; set; } = new(StringComparer.Ordinal);
}

public static class GraphSubjectKinds
{
    public const string Method = "method";
    public const string File = "file";
    public const string Project = "project";
    public const string Edge = "edge";
}

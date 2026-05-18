namespace Core.Graph;

public class GraphNode
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public string ProjectPath { get; set; } = "";

    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    public bool IsExternal { get; set; }

    /// <summary>由构建阶段物化的上游调用方 Id（B ← A）。</summary>
    public List<string> CalledBy { get; set; } = new();

    /// <summary>可扩展元数据（路由入口、EF、MediatR、RAG 等）。</summary>
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);
}

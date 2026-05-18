namespace Core.Graph;

public class GraphNode
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    public bool IsExternal { get; set; }

    /// <summary>调用当前方法的上游方法 Id 列表（B ← A）。</summary>
    public List<string> CalledBy { get; set; } = new();
}

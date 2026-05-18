namespace Core.Graph.Analysis;

public sealed class GraphFact
{
    public string Analyzer { get; set; } = "";

    public string SubjectId { get; set; } = "";

    public string SubjectKind { get; set; } = GraphSubjectKinds.Method;

    public string FactType { get; set; } = "";

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

namespace Core.Graph.Analysis;

public sealed class GraphAnnotation
{
    public string Analyzer { get; set; } = "";

    public string TargetMethodId { get; set; } = "";

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public string? SourceFile { get; set; }
}

namespace Core.Export.Dtos;

public sealed class ExportNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Kind { get; init; }

    public string? Layer { get; init; }
    public string? NodeType { get; init; }

    public Dictionary<string, string>? Attributes { get; init; }
    public string? ProjectName { get; init; }
    public string? Namespace { get; init; }
    public string? ClassName { get; init; }
    public string? MethodName { get; init; }
    public bool IsExternal { get; init; }

    public ViewNodeState ViewState { get; init; } = ViewNodeState.Normal;

    public string? GroupId { get; init; }
}

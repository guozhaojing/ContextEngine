namespace Core.Export.Dtos;

public sealed class LayerLayout
{
    public string Type => "layered";
    public required string Direction { get; init; }
    public required IReadOnlyList<string> LayerOrder { get; init; }
    public LayoutSpacing? Spacing { get; init; }
}

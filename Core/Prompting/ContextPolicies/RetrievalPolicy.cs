// =============================================================================
// ContextPolicies/RetrievalPolicy.cs — configures how retrieval feeds into context
// =============================================================================

namespace Core.Prompting.ContextPolicies;

public sealed class RetrievalPolicy
{
    public int TopK { get; init; } = 10;
    public bool ExpandPaths { get; init; } = true;
    public double MinConfidence { get; init; } = 0.3;
    public bool IncludeLowConfidence { get; init; } = false;
    public IReadOnlyList<string> PreferredLayers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PreferredEntities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PreferredTables { get; init; } = Array.Empty<string>();

    public static RetrievalPolicy FromIntent(string intentCategory)
    {
        return intentCategory switch
        {
            "bug" => new RetrievalPolicy
            {
                TopK = 15,
                ExpandPaths = true,
                MinConfidence = 0.4,
                IncludeLowConfidence = false,
                PreferredLayers = new[] { "Service", "Repository" }
            },
            "feature" => new RetrievalPolicy
            {
                TopK = 10,
                ExpandPaths = true,
                MinConfidence = 0.3,
                PreferredLayers = new[] { "Route", "Controller", "Service", "Repository" }
            },
            "refactor" => new RetrievalPolicy
            {
                TopK = 20,
                ExpandPaths = true,
                MinConfidence = 0.3,
                IncludeLowConfidence = true,
                PreferredLayers = new[] { "Service", "Repository", "Method" }
            },
            "data" => new RetrievalPolicy
            {
                TopK = 15,
                ExpandPaths = true,
                MinConfidence = 0.2,
                PreferredLayers = new[] { "Repository", "Entity", "Table" }
            },
            "validation" => new RetrievalPolicy
            {
                TopK = 10,
                ExpandPaths = false,
                MinConfidence = 0.5,
                PreferredLayers = new[] { "Service", "Repository" }
            },
            _ => new RetrievalPolicy()
        };
    }
}

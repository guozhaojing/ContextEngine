// =============================================================================
// E2E/E2ECase.cs — end-to-end benchmark case definition
// =============================================================================

namespace Core.Evaluation.E2E;

public sealed class E2ECase
{
    public required string CaseId { get; init; }
    public required string Query { get; init; }
    public string? ExpectedIntent { get; init; }
    public IReadOnlyList<string> ExpectedEntities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedTables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedRoutes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedKeywords { get; init; } = Array.Empty<string>();
    public int MinSemanticPaths { get; init; } = 1;
    public int MinBusinessRules { get; init; } = 1;
    public int MinCompressedMethods { get; init; } = 1;
    public double MinCoherenceScore { get; init; } = 0.3;
    public double MinCompletenessScore { get; init; } = 0.3;
    public double MinStructuralScore { get; init; } = 0.3;
    public double MinActionabilityScore { get; init; } = 0.2;
    public IReadOnlyList<string> RequiredPromptSections { get; init; } = Array.Empty<string>();
}

// =============================================================================
// Validation/SemanticRegressionCase.cs — regression test case definition
// =============================================================================
// Defines a single regression test: query + expected behavior.
// Each case verifies that a specific error does NOT recur.
// =============================================================================

using Core.Retrieval.Retrieval;

namespace Core.Validation;

public sealed class SemanticRegressionCase
{
    public required string CaseId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required RegressionCategory Category { get; init; }
    public required RetrievalQuery Query { get; init; }

    public required IReadOnlyList<string> ExpectedSectionKinds { get; init; }
    public required IReadOnlyList<string> ForbiddenSectionTitles { get; init; }
    public required IReadOnlyList<string> RequiredChunkIds { get; init; }
    public required IReadOnlyList<string> ForbiddenPatterns { get; init; }

    public double MinGroundingScore { get; init; } = 0.7;
    public int MaxUngroundedSections { get; init; } = 0;
    public bool RequireSymbolBinding { get; init; } = true;
    public bool RequireSourceFile { get; init; } = true;

    public static SemanticRegressionCase CreateHallucinationTest(string query)
    {
        return new SemanticRegressionCase
        {
            CaseId = $"hall-{Guid.NewGuid():N}"[..8],
            Name = "No Hallucinated Titles",
            Description = $"Query '{query}' must not produce hallucinated section titles.",
            Category = RegressionCategory.Hallucination,
            Query = new RetrievalQuery { Query = query, TopK = 10 },
            ExpectedSectionKinds = Array.Empty<string>(),
            ForbiddenSectionTitles = new[]
            {
                "business logic abstraction",
                "core domain concept",
                "primary workflow",
                "enterprise pattern",
                "architectural pattern",
                "ontology completion",
            },
            RequiredChunkIds = Array.Empty<string>(),
            ForbiddenPatterns = Array.Empty<string>(),
            MinGroundingScore = 0.7,
        };
    }

    public static SemanticRegressionCase CreateGroundingTest(string query, int expectedSections)
    {
        return new SemanticRegressionCase
        {
            CaseId = $"gnd-{Guid.NewGuid():N}"[..8],
            Name = "Full Grounding Required",
            Description = $"Query '{query}' must have {expectedSections} fully grounded sections.",
            Category = RegressionCategory.Grounding,
            Query = new RetrievalQuery { Query = query, TopK = 10 },
            ExpectedSectionKinds = Array.Empty<string>(),
            ForbiddenSectionTitles = Array.Empty<string>(),
            RequiredChunkIds = Array.Empty<string>(),
            ForbiddenPatterns = Array.Empty<string>(),
            MinGroundingScore = 0.8,
            MaxUngroundedSections = 0,
            RequireSymbolBinding = true,
        };
    }

    public static SemanticRegressionCase CreateDeterminismTest(string query)
    {
        return new SemanticRegressionCase
        {
            CaseId = $"det-{Guid.NewGuid():N}"[..8],
            Name = "Deterministic Retrieval",
            Description = $"Query '{query}' must produce identical results on re-run.",
            Category = RegressionCategory.Determinism,
            Query = new RetrievalQuery { Query = query, TopK = 10 },
            ExpectedSectionKinds = Array.Empty<string>(),
            ForbiddenSectionTitles = Array.Empty<string>(),
            RequiredChunkIds = Array.Empty<string>(),
            ForbiddenPatterns = Array.Empty<string>(),
            RequireSymbolBinding = false,
        };
    }
}

public enum RegressionCategory
{
    Hallucination,
    Grounding,
    Determinism,
    EntityIntegrity,
    SymbolStability,
    PathIntegrity,
}

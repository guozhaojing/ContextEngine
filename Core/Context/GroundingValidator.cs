// =============================================================================
// Context/GroundingValidator.cs — validates context grounding quality
// =============================================================================
// Checks:
//   - Section titles are derived from sources (not hallucinated)
//   - Content is traceable to graph nodes or chunks
//   - No LLM-completed abstractions
//   - All entity/table references are grounded
// =============================================================================

using Core.Graph;
using Core.Retrieval.Chunking;
using Core.Semantics;
using Core.Truth;

namespace Core.Context;

public sealed class GroundingValidator
{
    private readonly GroundingValidatorOptions _options;

    public GroundingValidator(GroundingValidatorOptions? options = null)
    {
        _options = options ?? GroundingValidatorOptions.Default;
    }

    public GroundingValidationResult Validate(ContextSection section, List<CodeChunk> sourceChunks)
    {
        var issues = new List<GroundingIssue>();

        ValidateTitle(section, sourceChunks, issues);
        ValidateContentSources(section, sourceChunks, issues);
        ValidateEntityReferences(section, sourceChunks, issues);
        ValidateNoHallucination(section, issues);

        var score = CalculateGroundingScore(section, sourceChunks, issues);

        return new GroundingValidationResult
        {
            SectionTitle = section.Title,
            SectionKind = section.Kind,
            GroundingScore = score,
            IsFullyGrounded = score >= _options.FullyGroundedThreshold,
            Issues = issues,
        };
    }

    private void ValidateTitle(
        ContextSection section,
        List<CodeChunk> chunks,
        List<GroundingIssue> issues)
    {
        var title = section.Title;

        if (title.Contains("??") || title.Contains("..."))
        {
            issues.Add(new GroundingIssue
            {
                IssueType = GroundingIssueType.HallucinatedTitle,
                Description = $"Section title '{title}' contains placeholder markers.",
                Severity = 0.8,
            });
        }

        var hasChunkEvidence = chunks.Any(c =>
            title.Contains(c.Title, StringComparison.OrdinalIgnoreCase)
            || title.Contains(c.ChunkId));

        if (!hasChunkEvidence && section.Kind != ContextSectionKind.StructuredSummary)
        {
            issues.Add(new GroundingIssue
            {
                IssueType = GroundingIssueType.UngroundedTitle,
                Description = $"Section title '{title}' not found in any source chunk.",
                Severity = 0.5,
            });
        }
    }

    private void ValidateContentSources(
        ContextSection section,
        List<CodeChunk> chunks,
        List<GroundingIssue> issues)
    {
        var content = section.Content;
        var referencedChunkCount = 0;

        foreach (var chunk in chunks)
        {
            if (content.Contains(chunk.ChunkId) || content.Contains(chunk.Title))
                referencedChunkCount++;
        }

        if (referencedChunkCount == 0 && chunks.Count > 0)
        {
            issues.Add(new GroundingIssue
            {
                IssueType = GroundingIssueType.DisconnectedContent,
                Description = $"Section content has no reference to any of the {chunks.Count} source chunks.",
                Severity = 0.6,
            });
        }
    }

    private void ValidateEntityReferences(
        ContextSection section,
        List<CodeChunk> chunks,
        List<GroundingIssue> issues)
    {
        var chunkEntities = new HashSet<string>(
            chunks.SelectMany(c => c.EntityNames),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entityName in chunkEntities)
        {
            if (!section.Content.Contains(entityName, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new GroundingIssue
                {
                    IssueType = GroundingIssueType.MissingEntityReference,
                    Description = $"Entity '{entityName}' from source chunks not found in section content.",
                    Severity = 0.3,
                });
            }
        }
    }

    private void ValidateNoHallucination(
        ContextSection section,
        List<GroundingIssue> issues)
    {
        var hallmarkedPatterns = new[]
        {
            "business logic abstraction",
            "core domain concept",
            "primary workflow",
            "enterprise pattern",
            "architectural pattern",
        };

        foreach (var pattern in hallmarkedPatterns)
        {
            if (section.Content.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                && section.Title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new GroundingIssue
                {
                    IssueType = GroundingIssueType.OntologyCompletion,
                    Description = $"Section appears to use LLM-completed abstraction: '{pattern}' without source evidence.",
                    Severity = 0.7,
                });
            }
        }
    }

    private double CalculateGroundingScore(
        ContextSection section,
        List<CodeChunk> chunks,
        List<GroundingIssue> issues)
    {
        var score = 1.0;

        foreach (var issue in issues)
            score -= issue.Severity * 0.25;

        if (chunks.Count == 0)
            score -= 0.5;

        if (section.SourceChunkIds.Count == 0)
            score -= 0.3;

        return Math.Max(0.0, score);
    }
}

public sealed class GroundingValidatorOptions
{
    public double FullyGroundedThreshold { get; init; } = 0.7;

    public static GroundingValidatorOptions Default => new();
}

public sealed class GroundingValidationResult
{
    public required string SectionTitle { get; init; }
    public ContextSectionKind SectionKind { get; init; }
    public double GroundingScore { get; init; }
    public bool IsFullyGrounded { get; init; }
    public required IReadOnlyList<GroundingIssue> Issues { get; init; }
}

public sealed class GroundingIssue
{
    public required GroundingIssueType IssueType { get; init; }
    public required string Description { get; init; }
    public double Severity { get; init; }
}

public enum GroundingIssueType
{
    HallucinatedTitle,
    UngroundedTitle,
    DisconnectedContent,
    MissingEntityReference,
    OntologyCompletion,
}

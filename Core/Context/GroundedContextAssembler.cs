// =============================================================================
// Context/GroundedContextAssembler.cs — grounded context assembly
// =============================================================================
// Extends ContextBuilder with strict grounding rules:
//   1. Section titles MUST come from graph nodes, chunks, or symbols
//   2. No LLM-generated section names
//   3. Each section carries SectionEvidence
//   4. Sections that cannot be grounded are dropped or marked
//   5. Full traceability from section to source symbol
// =============================================================================

using System.Text;
using Core.Context.Compression;
using Core.Graph;
using Core.Graph.Query;
using Core.Retrieval.Chunking;
using Core.Retrieval.Embedding;
using Core.Retrieval.Retrieval;
using Core.Semantics;
using Core.Truth;

namespace Core.Context;

public sealed class GroundedContextAssembler
{
    private readonly GraphQueryService _query;
    private readonly ContextCompression _compression;
    private readonly GroundingValidator _validator;
    private readonly PropagationLimiter _limiter;
    private readonly GroundedAssemblerOptions _options;

    public GroundedContextAssembler(
        GraphQueryService query,
        GroundedAssemblerOptions? options = null)
    {
        _query = query;
        _compression = new ContextCompression(query);
        _validator = new GroundingValidator();
        _limiter = new PropagationLimiter();
        _options = options ?? GroundedAssemblerOptions.Default;
    }

    public GroundedAssemblyResult Assemble(RetrievalResult retrievalResult)
    {
        var sections = new List<ContextSection>();
        var evidenceTrails = new List<SectionEvidence>();
        var rejectedSections = new List<RejectedSection>();

        if (retrievalResult.Candidates.Count == 0)
            return new GroundedAssemblyResult
            {
                Sections = Array.Empty<ContextSection>(),
                Evidence = Array.Empty<SectionEvidence>(),
                RejectedSections = Array.Empty<RejectedSection>(),
                TotalTokens = 0,
            };

        var chunks = retrievalResult.Candidates.Select(c => c.Chunk).ToList();

        var routeChunks = chunks.Where(c => c.Kind == ChunkKind.Route).ToList();
        foreach (var rc in routeChunks)
        {
            var (section, evidence) = BuildGroundedEntryPointSection(rc);
            if (TryAdmit(section, evidence, chunks, sections, evidenceTrails, rejectedSections))
                continue;
        }

        var pathChunks = chunks.Where(c => c.Kind == ChunkKind.SemanticPath).ToList();
        if (pathChunks.Count > 0)
        {
            var (section, evidence) = BuildGroundedCallChainSection(chunks, pathChunks);
            TryAdmit(section, evidence, chunks, sections, evidenceTrails, rejectedSections);
        }

        var entityChunks = chunks.Where(c => c.Kind == ChunkKind.EntityAccess).ToList();
        if (entityChunks.Count > 0)
        {
            var (section, evidence) = BuildGroundedEntityTableSection(entityChunks);
            TryAdmit(section, evidence, chunks, sections, evidenceTrails, rejectedSections);
        }

        var methodChunks = chunks.Where(c => c.Kind == ChunkKind.Method).ToList();
        if (methodChunks.Count > 0)
        {
            var (section, evidence) = BuildGroundedMethodsSection(methodChunks);
            TryAdmit(section, evidence, chunks, sections, evidenceTrails, rejectedSections);
        }

        var (summarySection, summaryEvidence) = BuildGroundedSummary(chunks, retrievalResult);
        TryAdmit(summarySection, summaryEvidence, chunks, sections, evidenceTrails, rejectedSections);

        return new GroundedAssemblyResult
        {
            Sections = sections,
            Evidence = evidenceTrails,
            RejectedSections = rejectedSections,
            TotalTokens = sections.Sum(s => s.TokenCount),
        };
    }

    private bool TryAdmit(
        ContextSection section,
        SectionEvidence evidence,
        List<CodeChunk> sourceChunks,
        List<ContextSection> sections,
        List<SectionEvidence> evidenceTrails,
        List<RejectedSection> rejectedSections)
    {
        var validation = _validator.Validate(section, sourceChunks);

        if (_options.EnforceGrounding && !validation.IsFullyGrounded)
        {
            rejectedSections.Add(new RejectedSection
            {
                Title = section.Title,
                Kind = section.Kind,
                Reason = $"Grounding score {validation.GroundingScore:F2} below threshold {_options.GroundingThreshold}.",
                ValidationIssues = validation.Issues.ToList(),
            });
            return false;
        }

        sections.Add(section);
        evidenceTrails.Add(evidence);
        return true;
    }

    private (ContextSection Section, SectionEvidence Evidence) BuildGroundedEntryPointSection(CodeChunk routeChunk)
    {
        var content = new StringBuilder();
        content.AppendLine($"**Entry Point**: {routeChunk.Title}");
        content.AppendLine($"**Source**: {string.Join(", ", routeChunk.SourceFiles.Take(3))}");
        content.AppendLine();

        if (routeChunk.Metadata is not null)
        {
            var m = routeChunk.Metadata;
            content.AppendLine($"**Connected Entities**: {(m.RelatedEntities.Count > 0 ? string.Join(", ", m.RelatedEntities) : "none")}");
            content.AppendLine($"**Connected Tables**: {(m.RelatedTables.Count > 0 ? string.Join(", ", m.RelatedTables) : "none")}");
            content.AppendLine($"**Data Access Distance**: {m.DataAccessDistance}h");
        }

        var text = content.ToString();
        var title = $"Entry Point: {routeChunk.Title}";

        var evidence = SectionEvidence.FromChunks(title, ContextSectionKind.EntryPointDetail,
            new[] { routeChunk }, _query);

        var section = new ContextSection
        {
            Title = title,
            Content = text,
            Kind = ContextSectionKind.EntryPointDetail,
            Priority = 10,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = new[] { routeChunk.ChunkId },
            RelevanceScore = routeChunk.ImportanceScore / 10.0,
        };

        return (section, evidence);
    }

    private (ContextSection Section, SectionEvidence Evidence) BuildGroundedCallChainSection(
        List<CodeChunk> allChunks,
        List<CodeChunk> pathChunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Call chain from entry point to data access:");
        sb.AppendLine();

        foreach (var pc in pathChunks.Take(5))
        {
            sb.AppendLine($"  [{pc.NodeIds.Count - 1}h] {pc.Summary}");
        }

        var text = sb.ToString();
        var title = "Call Chain";

        var evidence = SectionEvidence.FromChunks(title, ContextSectionKind.CallChain,
            pathChunks, _query);

        var section = new ContextSection
        {
            Title = title,
            Content = text,
            Kind = ContextSectionKind.CallChain,
            Priority = 7,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = pathChunks.Select(c => c.ChunkId).ToList(),
            RelevanceScore = pathChunks.Count > 0 ? 1.0 : 0,
        };

        return (section, evidence);
    }

    private (ContextSection Section, SectionEvidence Evidence) BuildGroundedEntityTableSection(
        List<CodeChunk> entityChunks)
    {
        var entities = new HashSet<string>(StringComparer.Ordinal);
        var tables = new HashSet<string>(StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("Entities and database table mappings:");
        sb.AppendLine();

        foreach (var chunk in entityChunks)
        {
            foreach (var entity in chunk.EntityNames)
            {
                if (!entities.Add(entity)) continue;
                foreach (var t in chunk.TableNames) tables.Add(t);

                var sourceNodeIds = chunk.NodeIds;
                var grounding = DetermineEntityGrounding(chunk, entity);

                sb.AppendLine($"  **{entity}** → {string.Join(", ", chunk.TableNames)}");
                sb.AppendLine($"    Source: {grounding}");
                sb.AppendLine($"    Access paths: {chunk.EntryPoints.Count}");
                sb.AppendLine();
            }
        }

        var text = sb.ToString();
        var title = "Entity / Table Mapping";

        var evidence = SectionEvidence.FromChunks(title, ContextSectionKind.EntityTableSummary,
            entityChunks, _query);

        var section = new ContextSection
        {
            Title = title,
            Content = text,
            Kind = ContextSectionKind.EntityTableSummary,
            Priority = 8,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = entityChunks.Select(c => c.ChunkId).ToList(),
            RelevanceScore = entityChunks.Count > 0 ? 1.0 : 0,
        };

        return (section, evidence);
    }

    private static string DetermineEntityGrounding(CodeChunk chunk, string entityName)
    {
        if (chunk.SourceFiles.Count > 0)
            return $"file: {Path.GetFileName(chunk.SourceFiles[0])}";

        if (chunk.NodeIds.Count > 0)
            return $"graph node: {chunk.NodeIds[0]}";

        return "ungrounded";
    }

    private (ContextSection Section, SectionEvidence Evidence) BuildGroundedMethodsSection(
        List<CodeChunk> methodChunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Key methods:");
        sb.AppendLine();

        foreach (var chunk in methodChunks.OrderByDescending(c => c.ImportanceScore).Take(10))
        {
            var cr = MethodCompressor.Compress(chunk.Content, new[] { chunk.ChunkId });
            if (string.IsNullOrEmpty(cr.CompressedContent)) continue;

            sb.AppendLine($"### {chunk.Title}");
            if (chunk.SourceFiles.Count > 0)
                sb.AppendLine($"  Source: {Path.GetFileName(chunk.SourceFiles[0])}");
            sb.AppendLine(cr.CompressedContent);
            sb.AppendLine();
        }

        var text = sb.ToString();
        var title = "Compressed Methods";

        var evidence = SectionEvidence.FromChunks(title, ContextSectionKind.CompressedMethod,
            methodChunks, _query);

        var section = new ContextSection
        {
            Title = title,
            Content = text,
            Kind = ContextSectionKind.CompressedMethod,
            Priority = 6,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = methodChunks.Take(10).Select(c => c.ChunkId).ToList(),
            RelevanceScore = methodChunks.Count > 0 ? 1.0 : 0,
        };

        return (section, evidence);
    }

    private (ContextSection Section, SectionEvidence Evidence) BuildGroundedSummary(
        List<CodeChunk> chunks,
        RetrievalResult retrievalResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Query: " + retrievalResult.Query.Query);
        sb.AppendLine();
        sb.AppendLine($"Top {chunks.Count} relevant code contexts:");
        sb.AppendLine();

        for (var i = 0; i < Math.Min(chunks.Count, 15); i++)
        {
            var c = chunks[i];
            var score = i < retrievalResult.Candidates.Count
                ? retrievalResult.Candidates[i].FusedScore
                : 0;
            var groundedStr = c.NodeIds.Count > 0 ? "grounded" : "ungrounded";
            sb.AppendLine($"  #{i + 1} [{score:F3}] [{c.Kind}] {c.Title} ({groundedStr})");
        }

        var text = sb.ToString();
        var title = "Structured Summary";

        var evidence = SectionEvidence.FromChunks(title, ContextSectionKind.StructuredSummary,
            chunks, _query);

        var section = new ContextSection
        {
            Title = title,
            Content = text,
            Kind = ContextSectionKind.StructuredSummary,
            Priority = 3,
            TokenCount = TokenEstimator.Estimate(text),
            SourceChunkIds = chunks.Take(15).Select(c => c.ChunkId).ToList(),
            RelevanceScore = chunks.Count > 0 ? 1.0 : 0,
        };

        return (section, evidence);
    }
}

public sealed class GroundedAssemblerOptions
{
    public bool EnforceGrounding { get; init; } = true;
    public double GroundingThreshold { get; init; } = 0.7;

    public static GroundedAssemblerOptions Default => new();
}

public sealed class GroundedAssemblyResult
{
    public required IReadOnlyList<ContextSection> Sections { get; init; }
    public required IReadOnlyList<SectionEvidence> Evidence { get; init; }
    public required IReadOnlyList<RejectedSection> RejectedSections { get; init; }
    public int TotalTokens { get; init; }
}

public sealed class RejectedSection
{
    public required string Title { get; init; }
    public ContextSectionKind Kind { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<GroundingIssue> ValidationIssues { get; init; } = Array.Empty<GroundingIssue>();
}

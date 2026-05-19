// =============================================================================
// Context/SectionEvidence.cs — evidence trail for a single context section
// =============================================================================
// Each context section must be able to answer: "why does this content appear here?"
// SectionEvidence captures the full trace from source symbol → chunk → section.
// =============================================================================

using Core.Graph;
using Core.Retrieval.Chunking;
using Core.Semantics;
using Core.Truth;

namespace Core.Context;

public sealed class SectionEvidence
{
    public required string SectionTitle { get; init; }
    public required ContextSectionKind SectionKind { get; init; }
    public required IReadOnlyList<string> SourceChunkIds { get; init; }
    public required IReadOnlyList<string> SourceNodeIds { get; init; }
    public required IReadOnlyList<string> SourceSymbolHandles { get; init; }
    public required IReadOnlyList<string> SourceFiles { get; init; }
    public required EvidenceStrength EvidenceStrength { get; init; }
    public required bool IsGrounded { get; init; }
    public string? GroundingSource { get; init; }

    public string Describe()
    {
        var parts = new List<string>
        {
            $"Section: {SectionTitle} ({SectionKind})",
            $"Grounded: {IsGrounded}",
            $"Evidence: {EvidenceStrength}",
        };

        if (SourceFiles.Count > 0)
            parts.Add($"Source files: [{string.Join(", ", SourceFiles.Take(3))}]");

        if (SourceNodeIds.Count > 0)
            parts.Add($"Graph nodes: {SourceNodeIds.Count}");

        if (SourceSymbolHandles.Count > 0)
            parts.Add($"Symbol bindings: {SourceSymbolHandles.Count}");

        return string.Join(" | ", parts);
    }

    public static SectionEvidence FromChunks(
        string title,
        ContextSectionKind kind,
        IEnumerable<CodeChunk> chunks,
        GraphQueryService query)
    {
        var chunkList = chunks.ToList();
        var allNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var allSymbolHandles = new HashSet<string>(StringComparer.Ordinal);
        var allSourceFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in chunkList)
        {
            foreach (var nid in chunk.NodeIds)
                allNodeIds.Add(nid);

            foreach (var sf in chunk.SourceFiles)
                allSourceFiles.Add(sf);
        }

        foreach (var nid in allNodeIds)
        {
            var node = query.GetNode(nid);
            var handle = node?.Attributes.GetValueOrDefault("symbolHandle", "");
            if (!string.IsNullOrEmpty(handle))
                allSymbolHandles.Add(handle);
        }

        var bestEvidence = DetermineEvidence(chunkList, allNodeIds.Count, allSymbolHandles.Count);

        return new SectionEvidence
        {
            SectionTitle = title,
            SectionKind = kind,
            SourceChunkIds = chunkList.Select(c => c.ChunkId).ToList(),
            SourceNodeIds = allNodeIds.ToList(),
            SourceSymbolHandles = allSymbolHandles.ToList(),
            SourceFiles = allSourceFiles.ToList(),
            EvidenceStrength = bestEvidence,
            IsGrounded = allSymbolHandles.Count > 0 || allNodeIds.Count > 0,
            GroundingSource = allSymbolHandles.Count > 0
                ? "symbol"
                : allNodeIds.Count > 0 ? "graph-node" : null,
        };
    }

    private static EvidenceStrength DetermineEvidence(
        List<CodeChunk> chunks,
        int nodeCount,
        int symbolCount)
    {
        if (symbolCount > 0) return EvidenceStrength.SemanticDirect;
        if (nodeCount > 5) return EvidenceStrength.SyntaxDirect;
        if (chunks.Count > 0) return EvidenceStrength.SyntaxPattern;
        return EvidenceStrength.None;
    }
}

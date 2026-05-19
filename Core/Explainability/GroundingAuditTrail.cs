// =============================================================================
// Explainability/GroundingAuditTrail.cs — full context grounding audit trail
// =============================================================================
// Generates a complete audit trail from query to context section, answering:
// "Why does this section appear here?"
//
// Covers: Query → Retrieval → Filter → Traversal → Edge Selection → Ranking
//          → Compression → Context Section
// Each step has: source file, symbol, confidence, evidence, truth type.
// =============================================================================

using Core.Context;
using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Retrieval;
using Core.Retrieval.Retrieval;
using Core.Semantics;
using Core.Truth;

namespace Core.Explainability;

public sealed class GroundingAuditTrail
{
    public required string Query { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    public required IReadOnlyList<AuditSection> Sections { get; init; }

    public string GenerateFullAudit()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Grounding Audit Trail");
        sb.AppendLine($"Query: {Query}");
        sb.AppendLine($"Generated: {GeneratedAt:O}");
        sb.AppendLine();

        for (var i = 0; i < Sections.Count; i++)
        {
            var section = Sections[i];
            sb.AppendLine($"## Section {i + 1}: {section.SectionTitle} [{section.SectionKind}]");
            sb.AppendLine($"Grounded: {section.IsGrounded} | Evidence: {section.EvidenceStrength}");
            sb.AppendLine();

            sb.AppendLine("### Retrieval Source");
            foreach (var chunk in section.SourceChunks.Take(5))
            {
                sb.AppendLine($"  - Chunk: {chunk.ChunkId} ({chunk.Kind})");
                sb.AppendLine($"    Vector: {chunk.VectorScore:F3} | Fused: {chunk.FusedScore:F3}");
            }
            sb.AppendLine();

            sb.AppendLine("### Graph Path");
            foreach (var node in section.GraphNodes.Take(10))
            {
                sb.AppendLine($"  - {node.NodeLabel} [{node.NodeKind}]");
                sb.AppendLine($"    Source: {node.SourceFile}");
                sb.AppendLine($"    Symbol: {node.SymbolHandle}");
                sb.AppendLine($"    Truth: {node.TruthType} | Confidence: {node.ConfidenceScore:F2}");
            }
            sb.AppendLine();

            sb.AppendLine("### Edge Selection");
            foreach (var edge in section.SelectedEdges.Take(10))
            {
                sb.AppendLine($"  - {edge.FromLabel} → {edge.ToLabel} [{edge.Kind}]");
                sb.AppendLine($"    Confidence: {edge.Confidence} | Grounded: {edge.IsGrounded}");
                sb.AppendLine($"    Evidence: {edge.Evidence}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static GroundingAuditTrail Build(
        string query,
        RetrievalResult retrievalResult,
        IReadOnlyList<ContextSection> sections,
        GraphQueryService graphQuery,
        GraphIndex index)
    {
        var auditSections = new List<AuditSection>();

        foreach (var section in sections)
        {
            var sourceChunks = new List<AuditChunk>();
            var graphNodes = new List<AuditNode>();
            var edges = new List<AuditEdge>();

            foreach (var chunkId in section.SourceChunkIds)
            {
                var candidate = retrievalResult.Candidates
                    .FirstOrDefault(c => StringComparer.Ordinal.Equals(c.Chunk.ChunkId, chunkId));
                if (candidate is null) continue;

                sourceChunks.Add(new AuditChunk
                {
                    ChunkId = chunkId,
                    Kind = candidate.Chunk.Kind.ToString(),
                    VectorScore = candidate.VectorSimilarity,
                    FusedScore = candidate.FusedScore,
                });

                foreach (var nodeId in candidate.Chunk.NodeIds.Take(5))
                {
                    var node = graphQuery.GetNode(nodeId);
                    if (node is null) continue;

                    graphNodes.Add(new AuditNode
                    {
                        NodeId = nodeId,
                        NodeLabel = node.Label,
                        NodeKind = node.Kind,
                        SourceFile = node.SourceFile,
                        SymbolHandle = node.SymbolHandle,
                        TruthType = node.TruthType,
                        ConfidenceScore = node.Confidence,
                    });
                }
            }

            foreach (var nodeId in section.SourceNodeIds.Take(20))
            {
                var outEdges = index.EdgeIdx.OutgoingByKind;
                if (outEdges.TryGetValue(nodeId, out var nodeEdges))
                {
                    foreach (var edgeInfo in nodeEdges.Take(3))
                    {
                        var targetNode = graphQuery.GetNode(edgeInfo.ToId);
                        var sourceNode = graphQuery.GetNode(nodeId);

                        edges.Add(new AuditEdge
                        {
                            FromId = nodeId,
                            FromLabel = sourceNode?.Label ?? nodeId,
                            ToId = edgeInfo.ToId,
                            ToLabel = targetNode?.Label ?? edgeInfo.ToId,
                            Kind = edgeInfo.Kind,
                            Confidence = edgeInfo.Confidence,
                            Evidence = edgeInfo.Evidence,
                            IsGrounded = edgeInfo.Grounded,
                        });
                    }
                }
            }

            auditSections.Add(new AuditSection
            {
                SectionTitle = section.Title,
                SectionKind = section.Kind.ToString(),
                IsGrounded = section.IsGrounded,
                EvidenceStrength = section.Evidence?.EvidenceStrength.ToString() ?? "none",
                SourceChunks = sourceChunks,
                GraphNodes = graphNodes,
                SelectedEdges = edges,
            });
        }

        return new GroundingAuditTrail
        {
            Query = query,
            Sections = auditSections,
        };
    }
}

public sealed class AuditSection
{
    public required string SectionTitle { get; init; }
    public required string SectionKind { get; init; }
    public bool IsGrounded { get; init; }
    public string EvidenceStrength { get; init; } = "";
    public required IReadOnlyList<AuditChunk> SourceChunks { get; init; }
    public required IReadOnlyList<AuditNode> GraphNodes { get; init; }
    public required IReadOnlyList<AuditEdge> SelectedEdges { get; init; }
}

public sealed class AuditChunk
{
    public required string ChunkId { get; init; }
    public string Kind { get; init; } = "";
    public double VectorScore { get; init; }
    public double FusedScore { get; init; }
}

public sealed class AuditNode
{
    public required string NodeId { get; init; }
    public required string NodeLabel { get; init; }
    public string NodeKind { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public string SymbolHandle { get; init; } = "";
    public string TruthType { get; init; } = "";
    public double ConfidenceScore { get; init; }
}

public sealed class AuditEdge
{
    public required string FromId { get; init; }
    public required string FromLabel { get; init; }
    public required string ToId { get; init; }
    public required string ToLabel { get; init; }
    public string Kind { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Evidence { get; init; } = "";
    public bool IsGrounded { get; init; }
}

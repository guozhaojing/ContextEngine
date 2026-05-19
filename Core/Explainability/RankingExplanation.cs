// =============================================================================
// Explainability/RankingExplanation.cs — explains ranking decisions
// =============================================================================
// Provides detailed explanation for why a specific chunk/candidate was ranked
// at a given position. Covers: vector similarity, graph structure, symbol grounding,
// edge confidence, business signals.
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Retrieval.Chunking;
using Core.Retrieval.Retrieval;
using Core.Semantics;
using Core.Truth;

namespace Core.Explainability;

public sealed class RankingExplanation
{
    public required string ChunkId { get; init; }
    public required string ChunkTitle { get; init; }
    public int Position { get; init; }

    public double VectorScore { get; init; }
    public double GraphDistanceScore { get; init; }
    public double SymbolCertaintyScore { get; init; }
    public double EdgeConfidenceScore { get; init; }
    public double GroundingScore { get; init; }
    public double TraversalCoverage { get; init; }
    public double CompositeScore { get; init; }

    public required IReadOnlyList<EvidenceFactor> ActivatedFactors { get; init; }
    public required IReadOnlyList<EvidenceFactor> SuppressedFactors { get; init; }

    public string ExplainRanking()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"#{Position + 1}: {ChunkTitle} (score={CompositeScore:F4})");
        sb.AppendLine($"  ID: {ChunkId}");

        if (ActivatedFactors.Count > 0)
        {
            sb.AppendLine("  Boosted by:");
            foreach (var f in ActivatedFactors.Take(5))
                sb.AppendLine($"    + {f.Description} ({f.Weight:P0})");
        }

        if (SuppressedFactors.Count > 0)
        {
            sb.AppendLine("  Suppressed by:");
            foreach (var f in SuppressedFactors.Take(5))
                sb.AppendLine($"    - {f.Description} ({f.Weight:P0})");
        }

        sb.AppendLine($"  Scores: v={VectorScore:F3} g={GraphDistanceScore:F3} s={SymbolCertaintyScore:F3} e={EdgeConfidenceScore:F3} gr={GroundingScore:F3} t={TraversalCoverage:F3}");

        return sb.ToString();
    }

    public static RankingExplanation FromCandidate(
        RetrievalCandidate candidate,
        int position,
        GraphIndex? graphIndex = null)
    {
        var chunk = candidate.Chunk;
        var meta = chunk.Metadata;

        var activated = new List<EvidenceFactor>();
        var suppressed = new List<EvidenceFactor>();

        if (candidate.VectorSimilarity > 0.7)
            activated.Add(new EvidenceFactor { Description = "High vector similarity", Weight = 0.35 });
        else if (candidate.VectorSimilarity < 0.3)
            suppressed.Add(new EvidenceFactor { Description = "Low vector similarity", Weight = 0.35 });

        if (meta?.IsEntryPoint == true)
            activated.Add(new EvidenceFactor { Description = "Is an API entry point", Weight = 0.15 });

        if (meta?.IsEntityAccess == true)
            activated.Add(new EvidenceFactor { Description = "Has entity access", Weight = 0.15 });

        if (meta is not null && (meta.FanIn + meta.FanOut) > 20)
            activated.Add(new EvidenceFactor { Description = "High graph connectivity", Weight = 0.10 });

        if (chunk.NodeIds.Count == 0)
            suppressed.Add(new EvidenceFactor { Description = "No graph node references", Weight = 0.20 });

        if (chunk.SourceFiles.Count == 0)
            suppressed.Add(new EvidenceFactor { Description = "No source file references", Weight = 0.15 });

        var symbolCertainty = chunk.NodeIds.Count > 0 ? 0.5 : 0.0;
        var edgeConfidence = meta?.ConfidenceScore ?? 0.5;
        var grounding = chunk.SourceFiles.Count > 0 ? 0.7 : 0.3;
        var traversal = meta is not null ? Math.Min(1.0, (meta.FanIn + meta.FanOut) / 30.0) : 0.0;

        return new RankingExplanation
        {
            ChunkId = chunk.ChunkId,
            ChunkTitle = chunk.Title,
            Position = position,
            VectorScore = candidate.VectorSimilarity,
            GraphDistanceScore = candidate.GraphRelevance,
            SymbolCertaintyScore = symbolCertainty,
            EdgeConfidenceScore = edgeConfidence,
            GroundingScore = grounding,
            TraversalCoverage = traversal,
            CompositeScore = candidate.FusedScore,
            ActivatedFactors = activated,
            SuppressedFactors = suppressed,
        };
    }
}

public sealed class EvidenceFactor
{
    public required string Description { get; init; }
    public double Weight { get; init; }
}

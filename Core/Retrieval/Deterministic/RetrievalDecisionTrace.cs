// =============================================================================
// Retrieval/Deterministic/RetrievalDecisionTrace.cs — full retrieval decision trace
// =============================================================================
// Records every decision point in the retrieval pipeline so the system can answer:
// "Why was this chunk returned at this position?"
//
// Trace covers: Query → Vector Search → Fusion → Filter → Sort → Take → Expand
// =============================================================================

using Core.Retrieval.Retrieval;
using Core.Retrieval.VectorStore;
using Core.Truth;

namespace Core.Retrieval.Deterministic;

public sealed class RetrievalDecisionTrace
{
    public required string Query { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public IReadOnlyList<VectorDecisionStep> VectorSteps { get; init; } = Array.Empty<VectorDecisionStep>();
    public IReadOnlyList<FusionDecisionStep> FusionSteps { get; init; } = Array.Empty<FusionDecisionStep>();
    public IReadOnlyList<FilterDecision> FilteredOut { get; init; } = Array.Empty<FilterDecision>();
    public IReadOnlyList<SortDecision> SortRanking { get; init; } = Array.Empty<SortDecision>();
    public IReadOnlyList<ExpansionDecision> Expansions { get; init; } = Array.Empty<ExpansionDecision>();

    public int TotalVectorCandidates => VectorSteps.Count;
    public int TotalFusedCandidates => FusionSteps.Count;
    public int TotalFiltered => FilteredOut.Count;
    public int TotalExpanded => Expansions.Count;

    public string GenerateExplanation()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Retrieval Decision Trace");
        sb.AppendLine($"Query: {Query}");
        sb.AppendLine($"Timestamp: {Timestamp:O}");
        sb.AppendLine();

        sb.AppendLine($"### 1. Vector Search ({TotalVectorCandidates} candidates)");
        foreach (var step in VectorSteps.Take(10))
            sb.AppendLine($"  [{step.Rank}] {step.ChunkId} similarity={step.Similarity:F4}");

        sb.AppendLine();
        sb.AppendLine($"### 2. Fusion ({TotalFusedCandidates} fused)");
        foreach (var step in FusionSteps.Take(10))
            sb.AppendLine($"  {step.ChunkId} fused={step.FusedScore:F4} (v={step.VectorScore:F2} g={step.GraphScore:F2} b={step.BusinessScore:F2})");

        if (FilteredOut.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"### 3. Filtered Out ({TotalFiltered})");
            foreach (var f in FilteredOut.Take(5))
                sb.AppendLine($"  {f.ChunkId} reason={f.Reason}");
        }

        sb.AppendLine();
        sb.AppendLine($"### 4. Final Ranking ({SortRanking.Count})");
        for (var i = 0; i < Math.Min(SortRanking.Count, 15); i++)
        {
            var s = SortRanking[i];
            sb.AppendLine($"  #{i + 1} {s.ChunkId} score={s.Score:F4} [{s.ChunkKind}] {s.Title}");
        }

        return sb.ToString();
    }
}

public sealed class VectorDecisionStep
{
    public required string ChunkId { get; init; }
    public double Similarity { get; init; }
    public int Rank { get; init; }
}

public sealed class FusionDecisionStep
{
    public required string ChunkId { get; init; }
    public double VectorScore { get; init; }
    public double GraphScore { get; init; }
    public double BusinessScore { get; init; }
    public double SymbolScore { get; init; }
    public double GroundingScore { get; init; }
    public double FusedScore { get; init; }
    public EvidenceStrength Evidence { get; init; }
}

public sealed class FilterDecision
{
    public required string ChunkId { get; init; }
    public required string Reason { get; init; }
}

public sealed class SortDecision
{
    public int Position { get; init; }
    public required string ChunkId { get; init; }
    public required string Title { get; init; }
    public required string ChunkKind { get; init; }
    public double Score { get; init; }
}

public sealed class ExpansionDecision
{
    public required string SourceChunkId { get; init; }
    public required string ExpandedChunkId { get; init; }
    public required string SharedVia { get; init; }
    public double Score { get; init; }
}

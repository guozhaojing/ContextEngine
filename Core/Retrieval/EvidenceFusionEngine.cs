// =============================================================================
// Retrieval/EvidenceFusionEngine.cs — evidence-based score fusion
// =============================================================================
// Computes final retrieval scores by fusing multiple evidence dimensions:
//   - Vector embedding score (semantic similarity)
//   - Graph structure score (graph distance, centrality)
//   - Symbol grounding score (symbol certainty, binding strength)
//   - Edge confidence score (reliability of connecting edges)
//   - Business signal score (entry points, entity access)
//
// All dimensions are normalized and weighted by evidence strength.
// =============================================================================

using Core.Truth;
using Core.Retrieval.Chunking;
using Core.Retrieval.VectorStore;

namespace Core.Retrieval;

public static class EvidenceFusionEngine
{
    public static EvidenceFusionResult Fuse(
        VectorSearchResult vectorResult,
        CodeChunk chunk,
        EvidenceFusionContext context)
    {
        var vectorScore = NormalizeSimilarity(vectorResult.Similarity);

        var graphScore = ComputeGraphEvidence(chunk, context);
        var symbolScore = ComputeSymbolEvidence(chunk, context);
        var edgeScore = ComputeEdgeEvidence(chunk, context);
        var businessScore = ComputeBusinessEvidence(chunk, context);
        var groundingScore = ComputeGroundingEvidence(chunk, context);

        var weights = DetermineWeights(context);

        var fused = vectorScore * weights.VectorWeight
                    + graphScore * weights.GraphWeight
                    + symbolScore * weights.SymbolWeight
                    + edgeScore * weights.EdgeWeight
                    + businessScore * weights.BusinessWeight
                    + groundingScore * weights.GroundingWeight;

        var evidence = DetermineOverallEvidence(vectorScore, graphScore, symbolScore);

        return new EvidenceFusionResult
        {
            FusedScore = Math.Round(fused, 4),
            VectorScore = Math.Round(vectorScore, 4),
            GraphScore = Math.Round(graphScore, 4),
            SymbolScore = Math.Round(symbolScore, 4),
            EdgeScore = Math.Round(edgeScore, 4),
            BusinessScore = Math.Round(businessScore, 4),
            GroundingScore = Math.Round(groundingScore, 4),
            OverallEvidence = evidence,
            Weights = weights,
        };
    }

    private static double ComputeGraphEvidence(CodeChunk chunk, EvidenceFusionContext context)
    {
        if (chunk.Metadata is null) return 0;
        var m = chunk.Metadata;

        var score = 0.0;

        if (m.EntryPointDistance >= 0 && m.EntryPointDistance <= 2)
            score += 0.5;
        else if (m.EntryPointDistance <= 5)
            score += 0.25;

        if (m.DataAccessDistance >= 0 && m.DataAccessDistance <= 1)
            score += 0.5;
        else if (m.DataAccessDistance <= 3)
            score += 0.25;

        var totalFan = m.FanIn + m.FanOut;
        if (totalFan > 20) score += 0.3;
        else if (totalFan > 5) score += 0.15;

        return Math.Min(score, 1.0);
    }

    private static double ComputeSymbolEvidence(CodeChunk chunk, EvidenceFusionContext context)
    {
        if (chunk.NodeIds.Count == 0) return 0;

        var symbolRatio = (double)context.GroundedNodeCount / Math.Max(1, chunk.NodeIds.Count);

        return symbolRatio switch
        {
            >= 0.8 => 1.0,
            >= 0.5 => 0.7,
            >= 0.2 => 0.4,
            _ => 0.1,
        };
    }

    private static double ComputeEdgeEvidence(CodeChunk chunk, EvidenceFusionContext context)
    {
        if (context.AverageEdgeConfidence is null) return 0.5;
        return Math.Min(context.AverageEdgeConfidence.Value, 1.0);
    }

    private static double ComputeBusinessEvidence(CodeChunk chunk, EvidenceFusionContext context)
    {
        if (chunk.Metadata is null) return 0;
        var m = chunk.Metadata;

        var score = m.BusinessScore;

        if (m.IsEntryPoint) score += 0.15;
        if (m.IsEntityAccess) score += 0.1;

        var hasPreferredEntity = context.PreferredEntities
            ?.Any(e => chunk.EntityNames.Contains(e, StringComparer.OrdinalIgnoreCase)) == true;
        if (hasPreferredEntity) score += 0.2;

        var hasPreferredTable = context.PreferredTables
            ?.Any(t => chunk.TableNames.Contains(t, StringComparer.OrdinalIgnoreCase)) == true;
        if (hasPreferredTable) score += 0.15;

        return Math.Min(score, 1.0);
    }

    private static double ComputeGroundingEvidence(CodeChunk chunk, EvidenceFusionContext context)
    {
        var score = 0.0;
        if (chunk.SourceFiles.Count > 0) score += 0.4;
        if (chunk.NodeIds.Count > 0) score += 0.3;
        if (context.GroundedNodeCount > 0) score += 0.3;
        return Math.Min(score, 1.0);
    }

    private static EvidenceWeights DetermineWeights(EvidenceFusionContext context)
    {
        if (context.PreferSymbolGrounding)
        {
            return new EvidenceWeights
            {
                VectorWeight = 0.20,
                GraphWeight = 0.15,
                SymbolWeight = 0.30,
                EdgeWeight = 0.15,
                BusinessWeight = 0.10,
                GroundingWeight = 0.10,
            };
        }

        if (context.PreferGraphStructure)
        {
            return new EvidenceWeights
            {
                VectorWeight = 0.15,
                GraphWeight = 0.35,
                SymbolWeight = 0.15,
                EdgeWeight = 0.15,
                BusinessWeight = 0.10,
                GroundingWeight = 0.10,
            };
        }

        return EvidenceWeights.Default;
    }

    private static EvidenceStrength DetermineOverallEvidence(
        double vectorScore,
        double graphScore,
        double symbolScore)
    {
        if (symbolScore > 0.8) return EvidenceStrength.SemanticDirect;
        if (graphScore > 0.7) return EvidenceStrength.SemanticInferred;
        if (vectorScore > 0.5) return EvidenceStrength.SyntaxDirect;
        return EvidenceStrength.SyntaxPattern;
    }

    private static double NormalizeSimilarity(double raw) => (raw + 1.0) / 2.0;
}

public sealed class EvidenceFusionContext
{
    public int GroundedNodeCount { get; init; }
    public double? AverageEdgeConfidence { get; init; }
    public IReadOnlyList<string>? PreferredEntities { get; init; }
    public IReadOnlyList<string>? PreferredTables { get; init; }
    public bool PreferSymbolGrounding { get; init; }
    public bool PreferGraphStructure { get; init; }

    public EvidenceFusionContext With(
        int? groundedNodeCount = null,
        double? averageEdgeConfidence = null,
        IReadOnlyList<string>? preferredEntities = null,
        IReadOnlyList<string>? preferredTables = null,
        bool? preferSymbolGrounding = null,
        bool? preferGraphStructure = null)
    {
        return new EvidenceFusionContext
        {
            GroundedNodeCount = groundedNodeCount ?? GroundedNodeCount,
            AverageEdgeConfidence = averageEdgeConfidence ?? AverageEdgeConfidence,
            PreferredEntities = preferredEntities ?? PreferredEntities,
            PreferredTables = preferredTables ?? PreferredTables,
            PreferSymbolGrounding = preferSymbolGrounding ?? PreferSymbolGrounding,
            PreferGraphStructure = preferGraphStructure ?? PreferGraphStructure,
        };
    }
}

public sealed class EvidenceWeights
{
    public double VectorWeight { get; init; } = 0.35;
    public double GraphWeight { get; init; } = 0.25;
    public double SymbolWeight { get; init; } = 0.15;
    public double EdgeWeight { get; init; } = 0.10;
    public double BusinessWeight { get; init; } = 0.10;
    public double GroundingWeight { get; init; } = 0.05;

    public static EvidenceWeights Default => new();
}

public sealed class EvidenceFusionResult
{
    public double FusedScore { get; init; }
    public double VectorScore { get; init; }
    public double GraphScore { get; init; }
    public double SymbolScore { get; init; }
    public double EdgeScore { get; init; }
    public double BusinessScore { get; init; }
    public double GroundingScore { get; init; }
    public EvidenceStrength OverallEvidence { get; init; }
    public EvidenceWeights Weights { get; init; } = EvidenceWeights.Default;
}

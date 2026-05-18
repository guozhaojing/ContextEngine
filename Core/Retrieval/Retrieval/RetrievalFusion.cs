using Core.Retrieval.VectorStore;

namespace Core.Retrieval.Retrieval;

public static class RetrievalFusion
{
    public static double ComputeFusedScore(
        VectorSearchResult vectorResult,
        double graphRelevance,
        double businessRelevance,
        double importanceScore)
    {
        var vectorScore = NormalizeSimilarity(vectorResult.Similarity);

        // Weighted fusion
        var fused = vectorScore * 0.35      // semantic similarity
                    + graphRelevance * 0.25  // graph structure
                    + businessRelevance * 0.20 // business signals
                    + (importanceScore / 10.0) * 0.20; // importance

        return Math.Round(fused, 4);
    }

    public static double ComputeGraphRelevance(
        int entryPointDistance,
        int dataAccessDistance,
        int fanIn,
        int fanOut)
    {
        var score = 0.0;

        // Closer to entry point = more relevant for API queries
        if (entryPointDistance >= 0 && entryPointDistance <= 3)
            score += 0.4;
        else if (entryPointDistance > 3 && entryPointDistance <= 6)
            score += 0.2;

        // Closer to data = more relevant for data queries
        if (dataAccessDistance >= 0 && dataAccessDistance <= 2)
            score += 0.3;
        else if (dataAccessDistance > 2 && dataAccessDistance <= 4)
            score += 0.15;

        // Connectivity
        var totalFan = fanIn + fanOut;
        if (totalFan > 20) score += 0.3;
        else if (totalFan > 5) score += 0.15;

        return Math.Min(score, 1.0);
    }

    public static double ComputeBusinessRelevance(
        bool isEntryPoint,
        bool isEntityAccess,
        double businessScore,
        IReadOnlyList<string>? preferredEntities,
        IReadOnlyList<string>? preferredTables)
    {
        var score = businessScore;

        // Boost for preferred entities/tables
        if (preferredEntities?.Count > 0 && isEntityAccess)
            score += 0.2;

        if (isEntryPoint) score += 0.15;

        return Math.Min(score, 1.0);
    }

    private static double NormalizeSimilarity(double raw)
    {
        // Cosine similarity is in [-1, 1]; map to [0, 1]
        return (raw + 1.0) / 2.0;
    }
}

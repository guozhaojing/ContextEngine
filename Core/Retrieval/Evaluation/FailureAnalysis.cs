using Core.Retrieval.Retrieval;

namespace Core.Retrieval.Evaluation;

public static class FailureAnalysis
{
    public static IReadOnlyList<FailureReason> Analyze(
        BenchmarkCase benchmarkCase,
        RetrievalResult result,
        RetrievalMetrics metrics)
    {
        var failures = new List<FailureReason>();

        if (metrics.RecallAtK < benchmarkCase.MinRecall)
        {
            // Check why recall is low
            var retrieved = result.Candidates.Take(benchmarkCase.TopK).ToList();
            var expectedIds = new HashSet<string>(benchmarkCase.Expected.ChunkIds, StringComparer.Ordinal);

            // Check if expected chunks exist in system at all
            var missingInSystem = expectedIds.Count(id =>
                !result.Candidates.Any(c => c.Chunk.ChunkId == id));

            if (missingInSystem == expectedIds.Count && expectedIds.Count > 0)
            {
                failures.Add(FailureReason.MissingChunkInSystem);
            }

            // Check vector similarity
            var topVectorSim = retrieved.FirstOrDefault()?.VectorSimilarity ?? 0;
            if (topVectorSim < 0.3)
                failures.Add(FailureReason.VectorSimilarityLow);

            // Check business signals
            var avgBusiness = retrieved.Count > 0
                ? retrieved.Average(c => c.BusinessRelevance)
                : 0;
            if (avgBusiness < 0.1)
                failures.Add(FailureReason.BusinessSignalTooLow);

            // Check graph expansion
            var avgGraph = retrieved.Count > 0
                ? retrieved.Average(c => c.GraphRelevance)
                : 0;
            if (avgGraph < 0.1)
                failures.Add(FailureReason.GraphExpansionInsufficient);

            // Check if top result has correct score ordering but wrong candidate
            if (retrieved.Count >= 2 &&
                retrieved[0].FusedScore < retrieved[1].FusedScore)
            {
                failures.Add(FailureReason.RankingWeightMismatch);
            }

            // Check entity edge
            if (benchmarkCase.Expected.EntityNames.Count > 0 &&
                metrics.EntityCoverage < 0.5)
            {
                failures.Add(FailureReason.EntityEdgeMissing);
            }

            // Semantic dilution: too many irrelevant high-score results
            var relevantInTop = retrieved.Take(5).Count(c =>
                expectedIds.Contains(c.Chunk.ChunkId));
            if (retrieved.Count >= 5 && relevantInTop == 0)
                failures.Add(FailureReason.SemanticDilution);
        }

        if (benchmarkCase.Expected.ChunkIds.Count > 3 && metrics.RecallAtK < 0.1)
            failures.Add(FailureReason.QueryTooGeneric);

        return failures;
    }
}

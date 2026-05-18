using System.Diagnostics;
using Core.Retrieval.Retrieval;

namespace Core.Retrieval.Evaluation;

public sealed class BenchmarkRunner
{
    private readonly HybridRetrievalEngine _engine;

    public BenchmarkRunner(HybridRetrievalEngine engine)
    {
        _engine = engine;
    }

    public async Task<RetrievalBenchmark> RunAsync(
        RetrievalBenchmark benchmark,
        CancellationToken ct = default)
    {
        var results = new List<BenchmarkResult>(benchmark.Cases.Count);
        var sw = new Stopwatch();

        foreach (var bc in benchmark.Cases)
        {
            ct.ThrowIfCancellationRequested();

            sw.Restart();
            var retrievalResult = await _engine.SearchAsync(new RetrievalQuery
            {
                Query = bc.Query,
                TopK = bc.TopK,
                PreferredEntities = bc.Expected.EntityNames.Count > 0 ? bc.Expected.EntityNames : null,
                PreferredTables = bc.Expected.TableNames.Count > 0 ? bc.Expected.TableNames : null,
                ExpandPaths = true
            }, ct);
            sw.Stop();

            var metrics = RetrievalMetrics.Compute(bc, retrievalResult);
            var failures = FailureAnalysis.Analyze(bc, retrievalResult, metrics);

            var topK = bc.TopK;
            var retrieved = retrievalResult.Candidates.Take(topK).ToList();

            var missedIds = new HashSet<string>(bc.Expected.ChunkIds, StringComparer.Ordinal);
            foreach (var c in retrieved)
                missedIds.Remove(c.Chunk.ChunkId);

            results.Add(new BenchmarkResult
            {
                Case = bc,
                Metrics = metrics,
                RetrievedChunkIds = retrieved.Select(c => c.Chunk.ChunkId).ToList(),
                RetrievedScores = retrieved.Select(c => c.FusedScore).ToList(),
                MissedExpectedIds = missedIds.ToList(),
                Failures = failures,
                SearchTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            });
        }

        // Compute aggregate
        var n = results.Count;
        var aggregate = n > 0 ? new AggregateMetrics
        {
            AvgRecall = Math.Round(results.Average(r => r.Metrics.RecallAtK), 4),
            AvgPrecision = Math.Round(results.Average(r => r.Metrics.PrecisionAtK), 4),
            AvgMRR = Math.Round(results.Average(r => r.Metrics.MRR), 4),
            AvgNDCG = Math.Round(results.Average(r => r.Metrics.NDCG), 4),
            HitRate = Math.Round((double)results.Count(r => r.Metrics.HitRate > 0) / n, 4),
            AvgLayerCoverage = Math.Round(results.Average(r => r.Metrics.LayerCoverage), 4),
            AvgEntityCoverage = Math.Round(results.Average(r => r.Metrics.EntityCoverage), 4),
            AvgRouteCoverage = Math.Round(results.Average(r => r.Metrics.RouteCoverage), 4),
            TotalFailures = results.Sum(r => r.Failures.Count),
            AvgSearchTimeMs = Math.Round(results.Average(r => r.SearchTimeMs), 2)
        } : null;

        return new RetrievalBenchmark
        {
            Name = benchmark.Name,
            Cases = benchmark.Cases,
            Results = results,
            Aggregate = aggregate
        };
    }
}

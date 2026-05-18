using Core.Retrieval.Retrieval;

namespace Core.Retrieval.Evaluation;

public sealed class RetrievalMetrics
{
    public double RecallAtK { get; init; }
    public double PrecisionAtK { get; init; }
    public double MRR { get; init; }
    public double NDCG { get; init; }
    public double HitRate { get; init; }
    public double LayerCoverage { get; init; }
    public double EntityCoverage { get; init; }
    public double RouteCoverage { get; init; }

    public static RetrievalMetrics Compute(
        BenchmarkCase benchmarkCase,
        RetrievalResult result)
    {
        var topK = benchmarkCase.TopK;
        var retrieved = result.Candidates.Take(topK).Select(c => c.Chunk).ToList();
        var retrievedIds = new HashSet<string>(retrieved.Select(c => c.ChunkId), StringComparer.Ordinal);

        var expected = benchmarkCase.Expected;
        var expectedIds = new HashSet<string>(expected.ChunkIds, StringComparer.Ordinal);
        var expectedMethods = new HashSet<string>(expected.MethodLabels, StringComparer.OrdinalIgnoreCase);
        var expectedEntities = new HashSet<string>(expected.EntityNames, StringComparer.OrdinalIgnoreCase);
        var expectedTables = new HashSet<string>(expected.TableNames, StringComparer.OrdinalIgnoreCase);
        var expectedLayers = new HashSet<string>(expected.LayerNames, StringComparer.OrdinalIgnoreCase);

        // Recall@K: how many expected chunks are in top-K results
        var hitCount = expectedIds.Count(id => retrievedIds.Contains(id));
        var recall = expectedIds.Count > 0 ? (double)hitCount / expectedIds.Count : 0;

        // Precision@K: how many retrieved are relevant
        var relevantInTop = retrieved.Count(c =>
            expectedIds.Contains(c.ChunkId) ||
            (expectedMethods.Count > 0 && c.NodeIds.Any(nid => IsMethodMatch(nid, c, expectedMethods))) ||
            (expectedEntities.Count > 0 && expectedEntities.Overlaps(c.EntityNames)) ||
            (expectedTables.Count > 0 && expectedTables.Overlaps(c.TableNames)));

        var precision = retrieved.Count > 0 ? (double)relevantInTop / retrieved.Count : 0;

        // MRR: 1 / rank of first relevant chunk
        var mrr = 0.0;
        for (var i = 0; i < retrieved.Count; i++)
        {
            if (expectedIds.Contains(retrieved[i].ChunkId) ||
                ExpectedOverlaps(retrieved[i], expected))
            {
                mrr = 1.0 / (i + 1);
                break;
            }
        }

        // NDCG: normalized discounted cumulative gain (relevant=3, partial=1, not=0)
        var dcg = 0.0;
        for (var i = 0; i < retrieved.Count; i++)
        {
            var gain = GetRelevanceGain(retrieved[i], expected);
            dcg += gain / Math.Log2(i + 2); // log2(i+2) for 1-indexed
        }
        var idealGains = expectedIds.Count >= topK ? topK : expectedIds.Count;
        var idcg = 0.0;
        for (var i = 0; i < Math.Min(idealGains, topK) && i < expectedIds.Count; i++)
            idcg += 3.0 / Math.Log2(i + 2);
        var ndcg = idcg > 0 ? dcg / idcg : 0;

        // HitRate: 1 if any hit, else 0
        var hitRate = hitCount > 0 || relevantInTop > 0 ? 1.0 : 0.0;

        // Layer coverage
        var retrievedLayers = new HashSet<string>(retrieved.Select(c => c.Kind.ToString()), StringComparer.OrdinalIgnoreCase);
        var layerCoverage = expectedLayers.Count > 0
            ? (double)expectedLayers.Count(l => retrievedLayers.Contains(l)) / expectedLayers.Count
            : 1.0;

        // Entity coverage
        var retrievedEntities = new HashSet<string>(
            retrieved.SelectMany(c => c.EntityNames ?? Array.Empty<string>()),
            StringComparer.OrdinalIgnoreCase);
        var entityCoverage = expectedEntities.Count > 0
            ? (double)expectedEntities.Count(e => retrievedEntities.Contains(e)) / expectedEntities.Count
            : 1.0;

        // Route coverage
        var retrievedRoutes = new HashSet<string>(
            retrieved.SelectMany(c => c.RoutePatterns ?? Array.Empty<string>()),
            StringComparer.OrdinalIgnoreCase);
        var routeCoverage = expected.RoutePatterns.Count > 0
            ? (double)expected.RoutePatterns.Count(r => retrievedRoutes.Contains(r)) / expected.RoutePatterns.Count
            : 1.0;

        return new RetrievalMetrics
        {
            RecallAtK = Math.Round(recall, 4),
            PrecisionAtK = Math.Round(precision, 4),
            MRR = Math.Round(mrr, 4),
            NDCG = Math.Round(ndcg, 4),
            HitRate = Math.Round(hitRate, 4),
            LayerCoverage = Math.Round(layerCoverage, 4),
            EntityCoverage = Math.Round(entityCoverage, 4),
            RouteCoverage = Math.Round(routeCoverage, 4)
        };
    }

    private static bool ExpectedOverlaps(Chunking.CodeChunk chunk, BenchmarkExpected expected)
    {
        if (expected.EntityNames.Any(e =>
                chunk.EntityNames.Contains(e, StringComparer.OrdinalIgnoreCase))) return true;
        if (expected.TableNames.Any(t =>
                chunk.TableNames.Contains(t, StringComparer.OrdinalIgnoreCase))) return true;
        if (expected.RoutePatterns.Any(r =>
                chunk.RoutePatterns.Contains(r, StringComparer.OrdinalIgnoreCase))) return true;
        return false;
    }

    private static double GetRelevanceGain(Chunking.CodeChunk chunk, BenchmarkExpected expected)
    {
        if (expected.ChunkIds.Contains(chunk.ChunkId)) return 3.0;
        if (expected.EntityNames.Any(e => chunk.EntityNames.Contains(e, StringComparer.OrdinalIgnoreCase))) return 2.0;
        if (expected.TableNames.Any(t => chunk.TableNames.Contains(t, StringComparer.OrdinalIgnoreCase))) return 2.0;
        if (expected.RoutePatterns.Any(r => chunk.RoutePatterns.Contains(r, StringComparer.OrdinalIgnoreCase))) return 1.0;
        return 0.0;
    }

    private static bool IsMethodMatch(string nodeId, Chunking.CodeChunk chunk, HashSet<string> methods)
    {
        return methods.Any(m => chunk.Title.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}

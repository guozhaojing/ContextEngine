// =============================================================================
// SemanticDoc/SemanticBenchmarkRunner.cs — run + evaluate + save failures
// =============================================================================
using System.Text.Json;

namespace Core.Cognition.SemanticDoc;

public sealed class SemanticBenchmarkRunner
{
    private readonly HybridRetrievalService _retrieval;
    private readonly ReverseIndex _reverseIndex;
    private readonly string _failureDir;

    public SemanticBenchmarkRunner(
        HybridRetrievalService retrieval,
        ReverseIndex reverseIndex,
        string? failureDir = null)
    {
        _retrieval = retrieval;
        _reverseIndex = reverseIndex;
        _failureDir = failureDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContextEngine", "benchmark-results", "failures");
        Directory.CreateDirectory(_failureDir);
    }

    public BenchmarkRunResult Run(IReadOnlyList<SemanticBenchmarkCase> cases)
    {
        var runResult = new BenchmarkRunResult
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            TotalCases = cases.Count,
        };

        foreach (var mode in new[] { SearchMode.Hybrid, SearchMode.EmbeddingOnly, SearchMode.GraphOnly, SearchMode.KeywordOnly })
        {
            var modeResult = RunForMode(cases, mode);
            runResult.ModeResults[mode] = modeResult;
        }

        runResult.ComputeAverages();
        return runResult;
    }

    private ModeBenchmarkResult RunForMode(IReadOnlyList<SemanticBenchmarkCase> cases, SearchMode mode)
    {
        var result = new ModeBenchmarkResult { Mode = mode };
        var profile = RetrievalProfile.Default;

        foreach (var c in cases)
        {
            var topK = 10;
            var searchResult = _retrieval.Search(c.Query, _reverseIndex, profile, mode, c.QueryType);
            var searchResults = searchResult.Filtered;
            var topMethodNames = searchResults.Select(r => r.DisplayLabel).ToHashSet(StringComparer.Ordinal);

            var expectedCore = c.Expected.Where(e => e.Priority == 1).Select(e => e.MethodName).ToHashSet(StringComparer.Ordinal);
            var expectedAll = c.Expected.Select(e => e.MethodName).ToHashSet(StringComparer.Ordinal);

            var hitsCoreAt5 = 0;
            var hitsCoreAt10 = 0;
            var hitsAllAt5 = 0;
            var hitsAllAt10 = 0;
            int firstHitRank = -1;

            for (var i = 0; i < Math.Min(topK, searchResults.Count); i++)
            {
                var name = searchResults[i].MethodName;
                if (expectedCore.Contains(name))
                {
                    if (firstHitRank < 0) firstHitRank = i + 1;
                    if (i < 5) hitsCoreAt5++;
                    if (i < 10) hitsCoreAt10++;
                }
                if (expectedAll.Contains(name))
                {
                    if (i < 5) hitsAllAt5++;
                    if (i < 10) hitsAllAt10++;
                }
            }

            var recall5 = expectedCore.Count > 0 ? (double)hitsCoreAt5 / expectedCore.Count : 1;
            var recall10 = expectedCore.Count > 0 ? (double)hitsCoreAt10 / expectedCore.Count : 1;
            var mrr = firstHitRank > 0 ? 1.0 / firstHitRank : 0;

            // Precision@5 / NoiseRatio
            var top5 = searchResults.Take(5).ToList();
            var relevantIn5 = top5.Count(r => expectedAll.Contains(r.MethodName));
            var precision5 = top5.Count > 0 ? (double)relevantIn5 / top5.Count : 0;
            var noiseRatio = top5.Count > 0 ? 1.0 - precision5 : 1;

            var passed = recall5 >= c.MinRecallAt5 && noiseRatio <= c.MaxNoiseRatio;

            result.CaseResults.Add(new CaseBenchmarkResult
            {
                CaseId = c.CaseId,
                Query = c.Query,
                Difficulty = c.Difficulty,
                QueryType = c.QueryType,
                Recall5 = recall5,
                Recall10 = recall10,
                MRR = mrr,
                Precision5 = precision5,
                NoiseRatio = noiseRatio,
                TopResults = searchResults.Take(5).Select(r => r.DisplayLabel).ToList(),
                Passed = passed,
            });

            // Save failure snapshot
            if (!passed)
            {
                SaveFailureSnapshot(c, searchResults.Take(10).ToList(), mode, recall5, noiseRatio);
            }
        }

        result.ComputeAggregates();
        return result;
    }

    private void SaveFailureSnapshot(SemanticBenchmarkCase c, List<ScoredMethodResult> top, SearchMode mode, double recall, double noise)
    {
        try
        {
            var snapshot = new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                query = c.Query,
                caseId = c.CaseId,
                mode = mode.ToString(),
                difficulty = c.Difficulty.ToString(),
                queryType = c.QueryType.ToString(),
                recall5 = recall,
                noiseRatio = noise,
                topResults = top.Select(r => new { id = r.MethodId, name = r.DisplayLabel, score = r.CompositeScore, embedding = r.EmbeddingScore, graph = r.GraphScore, keyword = r.KeywordScore }),
                expected = c.Expected.Select(e => new { name = e.MethodName, priority = e.Priority }),
            };
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{c.CaseId}-{mode}.json";
            File.WriteAllText(Path.Combine(_failureDir, fileName), json);
        }
        catch { }
    }
}

// ═══════════════════════════════════════════════════════════════
// Result types
// ═══════════════════════════════════════════════════════════════

public sealed class BenchmarkRunResult
{
    public string Timestamp { get; init; } = "";
    public int TotalCases { get; init; }
    public Dictionary<SearchMode, ModeBenchmarkResult> ModeResults { get; } = new();

    public double AvgRecall5 { get; set; }
    public double AvgRecall10 { get; set; }
    public double AvgMRR { get; set; }
    public double AvgPrecision5 { get; set; }
    public double AvgNoiseRatio { get; set; }
    public double PassRate { get; set; }

    public void ComputeAverages()
    {
        var all = ModeResults.Values.SelectMany(m => m.CaseResults).ToList();
        if (all.Count == 0) return;
        AvgRecall5 = all.Average(r => r.Recall5);
        AvgRecall10 = all.Average(r => r.Recall10);
        AvgMRR = all.Average(r => r.MRR);
        AvgPrecision5 = all.Average(r => r.Precision5);
        AvgNoiseRatio = all.Average(r => r.NoiseRatio);
        PassRate = (double)all.Count(r => r.Passed) / all.Count;
    }

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Semantic Retrieval Benchmark");
        sb.AppendLine($"Timestamp: {Timestamp}");
        sb.AppendLine($"Total cases: {TotalCases}");
        sb.AppendLine();

        sb.AppendLine($"## Aggregate Metrics");
        sb.AppendLine();
        sb.AppendLine($"| Mode | Recall@5 | Recall@10 | MRR | Precision@5 | NoiseRatio | PassRate |");
        sb.AppendLine($"|---|---|---|---|---|---|---|");
        foreach (var (mode, mr) in ModeResults.OrderBy(kvp => kvp.Key))
        {
            sb.AppendLine($"| {mode} | {mr.AvgRecall5:P1} | {mr.AvgRecall10:P1} | {mr.AvgMRR:F2} | {mr.AvgPrecision5:P1} | {mr.AvgNoiseRatio:P1} | {mr.PassRate:P0} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Per-Mode Details");
        foreach (var (mode, mr) in ModeResults.OrderBy(kvp => kvp.Key))
        {
            sb.AppendLine();
            sb.AppendLine($"### {mode}");
            sb.AppendLine($"Recall@5={mr.AvgRecall5:P1} MRR={mr.AvgMRR:F2} Noise={mr.AvgNoiseRatio:P1} Pass={mr.PassCount}/{mr.TotalCount}");
            sb.AppendLine();

            sb.AppendLine("| Case | Difficulty | Recall@5 | MRR | Noise | Pass | Top1 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var cr in mr.CaseResults.OrderBy(r => r.Difficulty).ThenBy(r => r.CaseId, StringComparer.Ordinal))
            {
                var status = cr.Passed ? "✅" : "❌";
                sb.AppendLine($"| {cr.CaseId} | {cr.Difficulty} | {cr.Recall5:P0} | {cr.MRR:F2} | {cr.NoiseRatio:P0} | {status} | {cr.TopResults.FirstOrDefault() ?? "-"} |");
            }
        }

        return sb.ToString();
    }
}

public sealed class ModeBenchmarkResult
{
    public SearchMode Mode { get; init; }
    public List<CaseBenchmarkResult> CaseResults { get; } = new();
    public int TotalCount => CaseResults.Count;
    public int PassCount => CaseResults.Count(r => r.Passed);
    public double PassRate => TotalCount > 0 ? (double)PassCount / TotalCount : 0;
    public double AvgRecall5 => TotalCount > 0 ? CaseResults.Average(r => r.Recall5) : 0;
    public double AvgRecall10 => TotalCount > 0 ? CaseResults.Average(r => r.Recall10) : 0;
    public double AvgMRR => TotalCount > 0 ? CaseResults.Average(r => r.MRR) : 0;
    public double AvgPrecision5 => TotalCount > 0 ? CaseResults.Average(r => r.Precision5) : 0;
    public double AvgNoiseRatio => TotalCount > 0 ? CaseResults.Average(r => r.NoiseRatio) : 0;

    public void ComputeAggregates() { }
}

public sealed class CaseBenchmarkResult
{
    public required string CaseId { get; init; }
    public required string Query { get; init; }
    public BenchmarkDifficulty Difficulty { get; init; }
    public QueryType QueryType { get; init; }
    public double Recall5 { get; init; }
    public double Recall10 { get; init; }
    public double MRR { get; init; }
    public double Precision5 { get; init; }
    public double NoiseRatio { get; init; }
    public required IReadOnlyList<string> TopResults { get; init; }
    public bool Passed { get; init; }
}

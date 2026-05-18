using System.Text.Json;

namespace Core.Retrieval.Evaluation;

public sealed class BenchmarkExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string> SaveAsync(
        RetrievalBenchmark benchmark,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        outputDirectory ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "retrieval-benchmark.json");

        var export = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            benchmark.Name,
            caseCount = benchmark.CaseCount,
            aggregate = benchmark.Aggregate,
            results = benchmark.Results.Select(r => new
            {
                caseId = r.Case.CaseId,
                query = r.Case.Query,
                expectedChunks = r.Case.Expected.ChunkIds,
                metrics = r.Metrics,
                retrievedChunkIds = r.RetrievedChunkIds,
                retrievedScores = r.RetrievedScores,
                missedExpectedIds = r.MissedExpectedIds,
                failures = r.Failures.Select(f => f.ToString()),
                searchTimeMs = r.SearchTimeMs
            })
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);

        return Path.GetFullPath(outputPath);
    }
}

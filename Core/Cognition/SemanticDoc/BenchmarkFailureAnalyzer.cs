// =============================================================================
// SemanticDoc/BenchmarkFailureAnalyzer.cs — analyze failure patterns
// =============================================================================
// Purpose: Classify WHY retrieval failed. Not guesswork — statistics from data.
// =============================================================================

using System.Text.Json;

namespace Core.Cognition.SemanticDoc;

public sealed class BenchmarkFailureAnalyzer
{
    private readonly string _failureDir;

    public BenchmarkFailureAnalyzer(string? failureDir = null)
    {
        _failureDir = failureDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContextEngine", "benchmark-results", "failures");
    }

    public FailureAnalysisReport Analyze()
    {
        var report = new FailureAnalysisReport();
        if (!Directory.Exists(_failureDir)) return report;

        var files = Directory.GetFiles(_failureDir, "*.json");
        var patterns = new Dictionary<FailurePattern, int>();
        var noiseMethods = new Dictionary<string, int>(StringComparer.Ordinal);
        var noiseClasses = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var expected = root.GetProperty("expected").EnumerateArray()
                    .Select(e => e.GetProperty("name").GetString() ?? "").ToHashSet(StringComparer.Ordinal);
                var topResults = root.GetProperty("topResults").EnumerateArray().ToList();

                // Collect noise methods (top10 minus expected)
                foreach (var r in topResults)
                {
                    var name = r.GetProperty("name").GetString() ?? "";
                    var id = r.GetProperty("id").GetString() ?? "";

                    if (!expected.Contains(name) && !expected.Any(e => id.Contains(e, StringComparison.Ordinal)))
                    {
                        noiseMethods[name] = noiseMethods.GetValueOrDefault(name, 0) + 1;

                        // Extract class name from full label "ClassName.MethodName"
                        var parts = name.Split('.');
                        if (parts.Length >= 2)
                            noiseClasses[parts[0]] = noiseClasses.GetValueOrDefault(parts[0], 0) + 1;
                    }
                }

                // Classify pattern
                var queryType = root.TryGetProperty("queryType", out var qt) ? qt.GetString() : "";
                var mode = root.TryGetProperty("mode", out var md) ? md.GetString() : "";

                foreach (var r in topResults)
                {
                    var name = r.GetProperty("name").GetString() ?? "";
                    if (expected.Contains(name)) continue;
                    var pattern = ClassifyNoise(name, mode, queryType);
                    patterns[pattern] = patterns.GetValueOrDefault(pattern, 0) + 1;
                }
            }
            catch { }
        }

        report.TotalFailures = files.Length;
        report.PatternDistribution = patterns.OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        report.TopNoiseMethods = noiseMethods.OrderByDescending(kvp => kvp.Value).Take(10)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        report.TopNoiseClasses = noiseClasses.OrderByDescending(kvp => kvp.Value).Take(10)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return report;
    }

    private static FailurePattern ClassifyNoise(string methodLabel, string mode, string queryType)
    {
        var lower = methodLabel.ToLowerInvariant();

        // CRUD patterns
        if (lower.Contains(".save") || lower.Contains(".update") || lower.Contains(".delete")
            || lower.Contains(".remove") || lower.Contains(".get(") || lower.Contains(".load"))
            return FailurePattern.CRUDPollution;

        // DTO patterns
        if (lower.Contains("dto") || lower.Contains("result") || lower.Contains("request")
            || lower.Contains("response") || lower.Contains("mapper") || lower.Contains("copy"))
            return FailurePattern.DTOPollution;

        // Graph explosion: generic base classes
        if (lower.Contains("basebll") || lower.Contains("basedao") || lower.Contains("hibernatedaosupport")
            || lower.Contains("nhb.") || lower.Contains(".find<") || lower.Contains("loadall"))
            return FailurePattern.GraphExplosion;

        // Semantic drift: business term overlap
        if (queryType is "BusinessWorkflow" or "BugAnalysis" or "Architecture"
            && mode is "EmbeddingOnly" or "Hybrid")
            return FailurePattern.SemanticDrift;

        // Keyword missing
        if (mode == "KeywordOnly")
            return FailurePattern.MissingKeyword;

        // Ranking: correct answer present but ranked low (detected by the runner, not here)
        return FailurePattern.RankingFailure;
    }
}

public enum FailurePattern
{
    SemanticDrift = 0,
    CRUDPollution = 1,
    DTOPollution = 2,
    GraphExplosion = 3,
    MissingKeyword = 4,
    MissingBusinessTerm = 5,
    RankingFailure = 6,
}

public sealed class FailureAnalysisReport
{
    public int TotalFailures { get; set; }
    public Dictionary<FailurePattern, int> PatternDistribution { get; set; } = new();
    public Dictionary<string, int> TopNoiseMethods { get; set; } = new();
    public Dictionary<string, int> TopNoiseClasses { get; set; } = new();

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Failure Pattern Analysis");
        sb.AppendLine($"Total failure snapshots: {TotalFailures}");
        sb.AppendLine();

        sb.AppendLine("## Pattern Distribution");
        sb.AppendLine();
        var total = PatternDistribution.Values.Sum();
        foreach (var (pattern, count) in PatternDistribution.OrderByDescending(kvp => kvp.Value))
        {
            var pct = total > 0 ? (double)count / total : 0;
            sb.AppendLine($"- {pattern}: {count} ({pct:P0})");
        }
        sb.AppendLine();

        sb.AppendLine("## Top Noise Methods");
        foreach (var (name, count) in TopNoiseMethods)
            sb.AppendLine($"- {name}: {count}");

        sb.AppendLine();
        sb.AppendLine("## Top Noise Classes");
        foreach (var (cls, count) in TopNoiseClasses)
            sb.AppendLine($"- {cls}: {count}");

        return sb.ToString();
    }
}

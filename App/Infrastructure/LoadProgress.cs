// =============================================================================
// Infrastructure/LoadProgress.cs — progress reporting for repository loading
// =============================================================================
namespace App.Infrastructure;

public sealed class LoadProgress
{
    public string Stage { get; set; } = "";
    public int CurrentProject { get; set; }
    public int TotalProjects { get; set; }
    public int CurrentFile { get; set; }
    public int TotalFiles { get; set; }
    public string? CurrentFilePath { get; set; }
    public bool IsComplete { get; set; }
    public string? Error { get; set; }

    public double PercentComplete
    {
        get
        {
            return Stage switch
            {
                "discovering" => 0.05,
                "parsing" when TotalFiles > 0 => 0.05 + 0.30 * CurrentFile / TotalFiles,
                "resolving" when TotalFiles > 0 => 0.35 + 0.40 * CurrentFile / TotalFiles,
                "building_graph" => 0.75,
                "indexing_semantics" => 0.82,
                "analyzing" => 0.88,
                "caching" => 0.94,
                "complete" => 1.0,
                _ => 0,
            };
        }
    }
}

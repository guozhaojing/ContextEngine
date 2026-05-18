namespace Core.Context;

public sealed class ContextDocument
{
    public required string Id { get; init; }
    public required string Query { get; init; }
    public required IReadOnlyList<ContextSection> Sections { get; init; }
    public int TotalTokens => Sections.Sum(s => s.TokenCount);
    public int BudgetMax { get; init; }
    public int BudgetUsed { get; init; }
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public int SourceResultCount { get; init; }
    public double AverageCompressionRatio =>
        Sections.Count > 0 ? Sections.Average(s => s.CompressionRatio) : 1.0;
    public double AverageRelevance =>
        Sections.Count > 0 ? Sections.Average(s => s.RelevanceScore) : 0;
}

// =============================================================================
// Validation/DriftReplayRunner.cs — replay-based drift detection
// =============================================================================
// Records retrieval/context pipeline output, then replays to detect drift.
// Same graph + same query → must produce identical context sections.
// =============================================================================

using Core.Context;
using Core.Retrieval.Deterministic;
using Core.Retrieval.Retrieval;

namespace Core.Validation;

public sealed class DriftReplayRunner
{
    private readonly record struct ReplayRecord(
        string Query,
        int GraphNodes,
        int GraphEdges,
        int ChunkCount,
        IReadOnlyList<string> SectionTitles,
        IReadOnlyList<string> SectionKinds,
        IReadOnlyList<string> RankedChunkIds,
        IReadOnlyList<double> RankedScores,
        DateTime RecordedAt);

    private ReplayRecord? _baseline;

    public bool HasBaseline => _baseline is not null;

    public void RecordBaseline(
        RetrievalQuery query,
        int graphNodes,
        int graphEdges,
        int chunkCount,
        IReadOnlyList<RetrievalCandidate> candidates,
        IReadOnlyList<ContextSection> sections)
    {
        _baseline = new ReplayRecord(
            query.Query,
            graphNodes,
            graphEdges,
            chunkCount,
            sections.Select(s => s.Title).ToList(),
            sections.Select(s => s.Kind.ToString()).ToList(),
            candidates.Select(c => c.Chunk.ChunkId).ToList(),
            candidates.Select(c => c.FusedScore).ToList(),
            DateTime.UtcNow);
    }

    public DriftReplayResult VerifyReplay(
        int graphNodes,
        int graphEdges,
        int chunkCount,
        IReadOnlyList<RetrievalCandidate> candidates,
        IReadOnlyList<ContextSection> sections)
    {
        if (_baseline is null)
        {
            return new DriftReplayResult
            {
                Passed = false,
                DriftDetected = true,
                Issues = new[] { "No baseline recorded." },
            };
        }

        var issues = new List<string>();

        if (_baseline.Value.GraphNodes != graphNodes)
            issues.Add($"Graph node count changed: {_baseline.Value.GraphNodes} → {graphNodes}.");

        if (_baseline.Value.GraphEdges != graphEdges)
            issues.Add($"Graph edge count changed: {_baseline.Value.GraphEdges} → {graphEdges}.");

        if (_baseline.Value.ChunkCount != chunkCount)
            issues.Add($"Chunk count changed: {_baseline.Value.ChunkCount} → {chunkCount}.");

        var candidateIds = candidates.Select(c => c.Chunk.ChunkId).ToList();
        if (!SequenceEqual(_baseline.Value.RankedChunkIds, candidateIds))
            issues.Add("Ranking order changed.");

        var sectionTitles = sections.Select(s => s.Title).ToList();
        if (!SequenceEqual(_baseline.Value.SectionTitles, sectionTitles))
            issues.Add("Context section titles changed.");

        var sectionKinds = sections.Select(s => s.Kind.ToString()).ToList();
        if (!SequenceEqual(_baseline.Value.SectionKinds, sectionKinds))
            issues.Add("Context section kinds changed.");

        return new DriftReplayResult
        {
            Passed = issues.Count == 0,
            DriftDetected = issues.Count > 0,
            Issues = issues,
            BaselineRecordedAt = _baseline.Value.RecordedAt.ToString("O"),
        };
    }

    private static bool SequenceEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!StringComparer.Ordinal.Equals(a[i], b[i]))
                return false;
        return true;
    }
}

public sealed class DriftReplayResult
{
    public bool Passed { get; init; }
    public bool DriftDetected { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public string BaselineRecordedAt { get; init; } = "";
}

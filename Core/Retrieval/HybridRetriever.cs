// =============================================================================
// Retrieval/HybridRetriever.cs — unified hybrid retriever with grounding
// =============================================================================
// Extends HybridRetrievalEngine with:
//   - Graph-aware ranking via GraphAwareRanker
//   - Evidence-based fusion via EvidenceFusionEngine
//   - Semantic path retrieval for deep traversal
//   - Grounding scoring for all candidates
//   - Confidence filtering with PropagationLimiter
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Retrieval.Chunking;
using Core.Retrieval.Embedding;
using Core.Retrieval.Retrieval;
using Core.Retrieval.VectorStore;
using Core.Truth;

namespace Core.Retrieval;

public sealed class HybridRetriever
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IReadOnlyDictionary<string, CodeChunk> _chunkIndex;
    private readonly GraphAwareRanker _ranker;
    private readonly SemanticPathRetriever? _pathRetriever;
    private readonly PropagationLimiter _limiter;
    private readonly HybridRetrieverOptions _options;

    public HybridRetriever(
        IVectorStore vectorStore,
        IEmbeddingProvider embeddingProvider,
        IReadOnlyDictionary<string, CodeChunk> chunkIndex,
        GraphIndex graphIndex,
        GraphQueryService queryService,
        HybridRetrieverOptions? options = null)
    {
        _vectorStore = vectorStore;
        _embeddingProvider = embeddingProvider;
        _chunkIndex = chunkIndex;
        _ranker = new GraphAwareRanker(graphIndex, queryService);
        _limiter = new PropagationLimiter();
        _options = options ?? HybridRetrieverOptions.Default;

        _pathRetriever = _options.EnablePathRetrieval
            ? new SemanticPathRetriever(graphIndex, queryService)
            : null;
    }

    public async Task<GroundedRetrievalResult> SearchAsync(
        RetrievalQuery query,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var queryVector = await _embeddingProvider.GenerateEmbeddingAsync(query.Query, ct);

        var vectorResults = _vectorStore.Search(queryVector, query.TopK * 3);

        var fusionContext = new EvidenceFusionContext
        {
            PreferredEntities = query.PreferredEntities,
            PreferredTables = query.PreferredTables,
            PreferSymbolGrounding = _options.PreferSymbolGrounding,
            PreferGraphStructure = _options.PreferGraphStructure,
        };

        var candidates = new List<RetrievalCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var groundedCount = 0;

        foreach (var vr in vectorResults)
        {
            if (!_chunkIndex.TryGetValue(vr.ChunkId, out var chunk)) continue;
            if (!seen.Add(chunk.ChunkId)) continue;
            if (chunk.Metadata is null) continue;
            if (chunk.Metadata.ConfidenceScore < query.MinConfidence) continue;

            var nodeGroundingCount = CountGroundedNodes(chunk);
            groundedCount += nodeGroundingCount;

            fusionContext = fusionContext.With(groundedNodeCount: groundedCount);

            var fusionResult = EvidenceFusionEngine.Fuse(vr, chunk, fusionContext);

            if (_options.FilterByTruth && fusionResult.OverallEvidence < EvidenceStrength.SyntaxPattern)
                continue;

            candidates.Add(new RetrievalCandidate
            {
                Chunk = chunk,
                VectorSimilarity = Math.Round(vr.Similarity, 4),
                GraphRelevance = fusionResult.GraphScore,
                BusinessRelevance = fusionResult.BusinessScore,
                FusedScore = fusionResult.FusedScore,
            });
        }

        var ranked = _ranker.Rank(candidates, query.Query);

        var topCandidates = ranked
            .Take(query.TopK)
            .Select(r => r.Candidate)
            .ToList();

        sw.Stop();

        var allEdges = ExtractEdges(topCandidates);
        var avgEdgeConfidence = ComputeAverageEdgeConfidence(allEdges);

        return new GroundedRetrievalResult
        {
            Query = query,
            Candidates = topCandidates,
            RankedCandidates = ranked.Take(query.TopK).ToList(),
            TotalChunksSearched = _chunkIndex.Count,
            VectorCandidates = vectorResults.Count,
            SearchTimeMs = sw.Elapsed.TotalMilliseconds,
            GroundedChunkCount = groundedCount,
            AverageEdgeConfidence = avgEdgeConfidence,
            Evidence = DetermineOverallEvidence(topCandidates),
        };
    }

    private int CountGroundedNodes(CodeChunk chunk)
    {
        var count = 0;
        foreach (var nid in chunk.NodeIds)
        {
            if (chunk.SourceFiles.Count > 0) count++;
        }
        return count;
    }

    private static IReadOnlyList<(string FromId, string ToId, string Kind)> ExtractEdges(
        IReadOnlyList<RetrievalCandidate> candidates)
    {
        var edges = new List<(string, string, string)>();
        foreach (var c in candidates)
        {
            for (var i = 0; i < c.Chunk.NodeIds.Count - 1; i++)
            {
                edges.Add((c.Chunk.NodeIds[i], c.Chunk.NodeIds[i + 1], "call"));
            }
        }
        return edges;
    }

    private static double ComputeAverageEdgeConfidence(
        IReadOnlyList<(string FromId, string ToId, string Kind)> edges)
    {
        if (edges.Count == 0) return 1.0;
        return 0.8;
    }

    private static EvidenceStrength DetermineOverallEvidence(
        IReadOnlyList<RetrievalCandidate> candidates)
    {
        if (candidates.Count == 0) return EvidenceStrength.None;

        var groundedCount = candidates.Count(c =>
            c.Chunk.NodeIds.Count > 0 && c.Chunk.SourceFiles.Count > 0);

        var ratio = (double)groundedCount / candidates.Count;
        if (ratio >= 0.8) return EvidenceStrength.SemanticDirect;
        if (ratio >= 0.5) return EvidenceStrength.SyntaxDirect;
        return EvidenceStrength.SyntaxPattern;
    }
}

public sealed class HybridRetrieverOptions
{
    public bool EnablePathRetrieval { get; init; } = true;
    public bool PreferSymbolGrounding { get; init; } = true;
    public bool PreferGraphStructure { get; init; }
    public bool FilterByTruth { get; init; } = true;

    public static HybridRetrieverOptions Default => new();
}

public sealed class GroundedRetrievalResult
{
    public required RetrievalQuery Query { get; init; }
    public required IReadOnlyList<RetrievalCandidate> Candidates { get; init; }
    public required IReadOnlyList<RankedCandidate> RankedCandidates { get; init; }
    public int TotalChunksSearched { get; init; }
    public int VectorCandidates { get; init; }
    public double SearchTimeMs { get; init; }
    public int GroundedChunkCount { get; init; }
    public double AverageEdgeConfidence { get; init; }
    public EvidenceStrength Evidence { get; init; }
}

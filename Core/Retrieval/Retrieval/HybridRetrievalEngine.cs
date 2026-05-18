using System.Diagnostics;
using Core.Retrieval.Chunking;
using Core.Retrieval.Embedding;
using Core.Retrieval.VectorStore;

namespace Core.Retrieval.Retrieval;

public sealed class HybridRetrievalEngine
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IReadOnlyDictionary<string, CodeChunk> _chunkIndex;

    public HybridRetrievalEngine(
        IVectorStore vectorStore,
        IEmbeddingProvider embeddingProvider,
        IReadOnlyDictionary<string, CodeChunk> chunkIndex)
    {
        _vectorStore = vectorStore;
        _embeddingProvider = embeddingProvider;
        _chunkIndex = chunkIndex;
    }

    public async Task<RetrievalResult> SearchAsync(
        RetrievalQuery query,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Generate query embedding
        var queryVector = await _embeddingProvider.GenerateEmbeddingAsync(query.Query, ct);

        // 2. Vector search (top K * 3 for recall)
        var vectorResults = _vectorStore.Search(queryVector, query.TopK * 3);

        // 3. Enrich with graph metadata + compute fused scores
        var candidates = new List<RetrievalCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vr in vectorResults)
        {
            if (!_chunkIndex.TryGetValue(vr.ChunkId, out var chunk)) continue;
            if (!seen.Add(chunk.ChunkId)) continue;

            var metadata = chunk.Metadata;
            if (metadata is null) continue;

            // Confidence filter
            if (metadata.ConfidenceScore < query.MinConfidence) continue;

            var graphRel = RetrievalFusion.ComputeGraphRelevance(
                metadata.EntryPointDistance,
                metadata.DataAccessDistance,
                metadata.FanIn,
                metadata.FanOut);

            var businessRel = RetrievalFusion.ComputeBusinessRelevance(
                metadata.IsEntryPoint,
                metadata.IsEntityAccess,
                metadata.BusinessScore,
                query.PreferredEntities,
                query.PreferredTables);

            var fused = RetrievalFusion.ComputeFusedScore(
                vr,
                graphRel,
                businessRel,
                chunk.ImportanceScore);

            candidates.Add(new RetrievalCandidate
            {
                Chunk = chunk,
                VectorSimilarity = Math.Round(vr.Similarity, 4),
                GraphRelevance = Math.Round(graphRel, 4),
                BusinessRelevance = Math.Round(businessRel, 4),
                FusedScore = fused
            });
        }

        // 4. Sort by fused score, then take top K
        candidates.Sort((a, b) => b.FusedScore.CompareTo(a.FusedScore));
        var topK = candidates.Take(query.TopK).ToList();

        // 5. Expand paths if requested
        if (query.ExpandPaths)
        {
            ExpandWithRelatedPaths(topK, seen, query.TopK);
        }

        // Re-sort after expansion
        topK.Sort((a, b) => b.FusedScore.CompareTo(a.FusedScore));
        topK = topK.Take(query.TopK).ToList();

        sw.Stop();

        return new RetrievalResult
        {
            Query = query,
            Candidates = topK,
            TotalChunksSearched = _chunkIndex.Count,
            VectorCandidates = vectorResults.Count,
            SearchTimeMs = sw.Elapsed.TotalMilliseconds
        };
    }

    private void ExpandWithRelatedPaths(
        List<RetrievalCandidate> candidates,
        HashSet<string> seen,
        int maxResults)
    {
        var toAdd = new List<RetrievalCandidate>();

        foreach (var candidate in candidates)
        {
            if (candidates.Count + toAdd.Count >= maxResults * 2) break;

            var chunk = candidate.Chunk;

            // Find upstream/downstream chunks via shared entity/table
            foreach (var table in chunk.Metadata?.RelatedTables ?? Array.Empty<string>())
            {
                foreach (var (id, other) in _chunkIndex)
                {
                    if (seen.Contains(id)) continue;
                    if (other.Metadata?.RelatedTables?.Contains(table, StringComparer.OrdinalIgnoreCase) == true)
                    {
                        seen.Add(id);
                        toAdd.Add(new RetrievalCandidate
                        {
                            Chunk = other,
                            VectorSimilarity = 0,
                            GraphRelevance = 0.5,
                            BusinessRelevance = 0.5,
                            FusedScore = other.ImportanceScore / 10.0 * 0.5
                        });
                    }
                }
            }
        }

        candidates.AddRange(toAdd);
    }
}

using System.Diagnostics;
using Core.Retrieval.Chunking;
using Core.Retrieval.Embedding;
using Core.Retrieval.VectorStore;
using Core.Truth;

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

        // 3. Enrich with graph metadata + compute fused scores via EvidenceFusion
        var candidates = new List<RetrievalCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var fusionContext = new EvidenceFusionContext
        {
            PreferredEntities = query.PreferredEntities,
            PreferredTables = query.PreferredTables,
        };

        foreach (var vr in vectorResults)
        {
            if (!_chunkIndex.TryGetValue(vr.ChunkId, out var chunk)) continue;
            if (!seen.Add(chunk.ChunkId)) continue;

            var metadata = chunk.Metadata;
            if (metadata is null) continue;

            // Confidence filter
            if (metadata.ConfidenceScore < query.MinConfidence) continue;

            var fusionResult = EvidenceFusionEngine.Fuse(vr, chunk, fusionContext);

            if (fusionResult.OverallEvidence < EvidenceStrength.SyntaxPattern)
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

        // 4. Sort by fused score, stable tie-break by ChunkId
        candidates = candidates
            .OrderByDescending(a => a.FusedScore)
            .ThenBy(a => a.Chunk.ChunkId, StringComparer.Ordinal)
            .ToList();
        var topK = candidates.Take(query.TopK).ToList();

        // 5. Expand paths if requested
        if (query.ExpandPaths)
        {
            ExpandWithRelatedPaths(topK, seen, query.TopK);
        }

        // Re-sort after expansion, stable tie-break
        topK = topK
            .OrderByDescending(a => a.FusedScore)
            .ThenBy(a => a.Chunk.ChunkId, StringComparer.Ordinal)
            .Take(query.TopK)
            .ToList();

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

        var sortedChunks = _chunkIndex
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (candidates.Count + toAdd.Count >= maxResults * 2) break;

            var chunk = candidate.Chunk;

            foreach (var table in (chunk.Metadata?.RelatedTables ?? Array.Empty<string>()).OrderBy(t => t, StringComparer.Ordinal))
            {
                foreach (var (id, other) in sortedChunks)
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

        toAdd = toAdd
            .OrderByDescending(a => a.FusedScore)
            .ThenBy(a => a.Chunk.ChunkId, StringComparer.Ordinal)
            .ToList();

        candidates.AddRange(toAdd);
    }
}

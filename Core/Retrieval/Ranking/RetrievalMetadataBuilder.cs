using Core.Graph;
using Core.Graph.Query;
using Core.Retrieval.Chunking;

namespace Core.Retrieval.Ranking;

public sealed class RetrievalMetadataBuilder
{
    private readonly GraphQueryService _query;
    private readonly CentralityAnalyzer _centrality;
    private readonly BusinessSignalAnalyzer _business;
    private readonly DependencyDepthAnalyzer _depth;

    public RetrievalMetadataBuilder(GraphQueryService query)
    {
        _query = query;
        _centrality = new CentralityAnalyzer(query);
        _business = new BusinessSignalAnalyzer(query);
        _depth = new DependencyDepthAnalyzer(query);
    }

    public IReadOnlyList<CodeChunk> Enrich(IReadOnlyList<CodeChunk> chunks)
    {
        // Collect all paths for path frequency computation
        var allPaths = CollectAllPaths(chunks);

        var enriched = new List<CodeChunk>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var metadata = BuildMetadata(chunk, allPaths);
            var factors = ComputeFactors(metadata);
            var newScore = factors.FinalScore * 10.0; // scale to 0-10

            enriched.Add(new CodeChunk
            {
                ChunkId = chunk.ChunkId,
                Kind = chunk.Kind,
                Title = chunk.Title,
                Summary = chunk.Summary,
                Content = chunk.Content,
                Keywords = chunk.Keywords,
                NodeIds = chunk.NodeIds,
                EdgeKinds = chunk.EdgeKinds,
                EntryPoints = chunk.EntryPoints,
                EntityNames = chunk.EntityNames,
                TableNames = chunk.TableNames,
                RoutePatterns = chunk.RoutePatterns,
                SourceFiles = chunk.SourceFiles,
                ImportanceScore = Math.Round(newScore, 2),
                TokenEstimate = chunk.TokenEstimate,
                Metadata = metadata
            });
        }

        return enriched;
    }

    private ChunkMetadata BuildMetadata(CodeChunk chunk, IEnumerable<SemanticPath> allPaths)
    {
        var nodeIds = chunk.NodeIds;

        // Aggregate per-node metrics
        var totalCallers = 0;
        var totalCallees = 0;
        var totalFanIn = 0;
        var totalFanOut = 0;
        var minEntryDist = int.MaxValue;
        var minDataDist = int.MaxValue;
        var maxLayerDepth = -1;
        var isEntryPoint = false;
        var isEntityAccess = false;
        var nodeCentralitySum = 0.0;
        var confidenceSum = 0.0;
        var confidenceCount = 0;

        foreach (var nid in nodeIds)
        {
            totalCallers += _centrality.GetCallerCount(nid);
            totalCallees += _centrality.GetCalleeCount(nid);

            var fanIn = _centrality.GetFanIn(nid);
            var fanOut = _centrality.GetFanOut(nid);
            totalFanIn = Math.Max(totalFanIn, fanIn);
            totalFanOut = Math.Max(totalFanOut, fanOut);

            nodeCentralitySum += _centrality.GetCentralityScore(nid);

            var ed = _depth.GetEntryPointDistance(nid);
            if (ed >= 0) minEntryDist = Math.Min(minEntryDist, ed);

            var dd = _depth.GetDataAccessDistance(nid);
            if (dd >= 0) minDataDist = Math.Min(minDataDist, dd);

            var ld = _depth.GetLayerDepth(nid);
            if (ld > maxLayerDepth) maxLayerDepth = ld;

            if (_business.IsEntryPoint(nid)) isEntryPoint = true;
            if (_business.IsEntityAccess(nid)) isEntityAccess = true;

            // Confidence from edges
            foreach (var calleeId in _query.GetCallees(nid))
            {
                var edge = _query.GetEdgeInfo(nid, calleeId);
                if (edge is not null)
                {
                    var conf = edge.Value.GetAttr("confidence");
                    if (!string.IsNullOrEmpty(conf))
                    {
                        confidenceSum += ConfidenceToDouble(conf);
                        confidenceCount++;
                    }
                }
            }
        }

        var avgCentrality = nodeIds.Count > 0 ? nodeCentralitySum / nodeIds.Count : 0;
        var pathFreq = _centrality.GetPathFrequency(nodeIds, allPaths);
        var confidenceScore = confidenceCount > 0 ? confidenceSum / confidenceCount : 0;

        // Business score for the whole chunk
        var businessScore = 0.0;
        if (nodeIds.Count > 0)
        {
            // Use the best-connected node in the chunk
            var bestNode = nodeIds.First();
            var bestBs = 0.0;
            foreach (var nid in nodeIds)
            {
                var bs = _business.GetBusinessScore(nid,
                    minEntryDist == int.MaxValue ? -1 : minEntryDist,
                    minDataDist == int.MaxValue ? -1 : minDataDist);
                if (bs > bestBs)
                {
                    bestBs = bs;
                    bestNode = nid;
                }
            }
            businessScore = bestBs;
        }

        // Dependency depth
        var depDepth = _depth.GetDependencyDepth(nodeIds);

        // Cross-project
        var projects = nodeIds
            .Select(nid => _query.GetNode(nid)?.ProjectName)
            .Where(p => p is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var isCrossProject = projects.Count > 1;

        return new ChunkMetadata
        {
            CallerCount = totalCallers,
            CalleeCount = totalCallees,
            FanIn = totalFanIn,
            FanOut = totalFanOut,
            EntryPointDistance = minEntryDist == int.MaxValue ? -1 : minEntryDist,
            DataAccessDistance = minDataDist == int.MaxValue ? -1 : minDataDist,
            CentralityScore = Math.Round(avgCentrality, 3),
            BusinessScore = Math.Round(businessScore, 3),
            LayerDepth = maxLayerDepth,
            DependencyDepth = depDepth,
            IsEntryPoint = isEntryPoint,
            IsEntityAccess = isEntityAccess,
            IsCrossProject = isCrossProject,
            RelatedTables = _business.GetRelatedTables(nodeIds),
            RelatedEntities = _business.GetRelatedEntities(nodeIds),
            RelatedRoutes = _business.GetRelatedRoutes(nodeIds),
            ConfidenceScore = Math.Round(confidenceScore, 3)
        };
    }

    private static ImportanceFactors ComputeFactors(ChunkMetadata m)
    {
        var centrality = m.CentralityScore;
        var business = m.BusinessScore;
        var traversal = m.FanIn + m.FanOut > 0
            ? Math.Min((double)(m.FanIn + m.FanOut) / 50.0, 1.0)
            : 0;

        var entryPoint = m.IsEntryPoint ? 1.0 : 0.0;
        var entityAccess = m.IsEntityAccess ? 1.0 : 0.0;

        var dependency = 0.0;
        if (m.EntryPointDistance >= 0 && m.EntryPointDistance <= 5)
            dependency += 0.5;
        if (m.DataAccessDistance >= 0 && m.DataAccessDistance <= 3)
            dependency += 0.5;

        // Weighted combination
        var final = centrality * 0.2
                    + business * 0.25
                    + traversal * 0.15
                    + entryPoint * 0.20
                    + entityAccess * 0.15
                    + dependency * 0.05;

        return new ImportanceFactors
        {
            CentralityFactor = Math.Round(centrality, 3),
            BusinessFactor = Math.Round(business, 3),
            TraversalFactor = Math.Round(traversal, 3),
            EntryPointFactor = Math.Round(entryPoint, 3),
            EntityAccessFactor = Math.Round(entityAccess, 3),
            DependencyFactor = Math.Round(dependency, 3),
            FinalScore = Math.Round(final, 3)
        };
    }

    private static IEnumerable<SemanticPath> CollectAllPaths(IReadOnlyList<CodeChunk> chunks)
    {
        // Reconstruct paths from SemanticPathChunks
        return chunks
            .Where(c => c.Kind == ChunkKind.SemanticPath)
            .Select(c => new SemanticPath
            {
                PathId = c.ChunkId.StartsWith("path:") ? c.ChunkId[5..] : c.ChunkId,
                NodeIds = c.NodeIds,
                EdgeKinds = c.EdgeKinds,
                Summary = c.Summary
            })
            .ToList();
    }

    private static double ConfidenceToDouble(string confidence)
    {
        return confidence.ToLowerInvariant() switch
        {
            "exact" => 1.0,
            "high" => 0.75,
            "medium" => 0.5,
            "low" => 0.25,
            _ => 0
        };
    }
}

// =============================================================================
// Compression/SemanticPathCompressor.cs — compress semantic paths for context
// =============================================================================

using System.Text;
using Core.Context.Models;
using Core.Graph;
using Core.Graph.Query;

namespace Core.Context.Compression;

public sealed class SemanticPathCompressor
{
    private readonly GraphQueryService _query;

    public SemanticPathCompressor(GraphQueryService query)
    {
        _query = query;
    }

    public ContextCompressionResult CompressPath(SemanticPath path, IReadOnlyList<string> sourceChunkIds)
    {
        var sb = new StringBuilder();
        var layers = new List<string>();
        var prevLayer = "";

        for (var i = 0; i < path.NodeIds.Count; i++)
        {
            var node = _query.GetNode(path.NodeIds[i]);
            var label = node?.Label ?? ShortenId(path.NodeIds[i]);
            var layer = InferLayer(node);
            layers.Add(layer);

            if (i == 0)
            {
                sb.Append($"[{layer}] {label}");
            }
            else
            {
                var edgeKind = i - 1 < path.EdgeKinds.Count ? path.EdgeKinds[i - 1] : "?";
                var edgeSymbol = edgeKind switch
                {
                    "call" => "→",
                    "nh:entity-access" => "⇢",
                    "spring:implements" => "⤖",
                    "spring:property-ref" => "·",
                    _ => "→"
                };

                if (layer != prevLayer)
                {
                    sb.Append($" {edgeSymbol}[{layer}] {label}");
                }
                else
                {
                    sb.Append($" {edgeSymbol} {label}");
                }
            }

            prevLayer = layer;
        }

        var original = path.Summary;
        var compressed = sb.ToString();

        return new ContextCompressionResult
        {
            OriginalContent = original,
            CompressedContent = compressed,
            OriginalTokens = Budgeting.ContextBudgetEstimator.Estimate(original),
            CompressedTokens = Budgeting.ContextBudgetEstimator.Estimate(compressed),
            Strategy = "SemanticPathCompressor",
            SourceChunkIds = sourceChunkIds
        };
    }

    public ContextCompressionResult CompressRoutes(IReadOnlyList<string> routeNodeIds, IReadOnlyList<string> sourceChunkIds)
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nodeId in routeNodeIds)
        {
            var node = _query.GetNode(nodeId);
            if (node is null) continue;

            var route = node.Attributes.GetValueOrDefault("route", "");
            var httpMethod = node.Attributes.GetValueOrDefault("http-method", "ANY");
            var key = $"{httpMethod} {route}";

            if (!seen.Add(key)) continue;

            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"[{httpMethod}] {route} → {node.Label}");
        }

        var compressed = sb.ToString();

        return new ContextCompressionResult
        {
            OriginalContent = string.Join("\n", routeNodeIds),
            CompressedContent = compressed,
            OriginalTokens = Budgeting.ContextBudgetEstimator.Estimate(string.Join("\n", routeNodeIds)),
            CompressedTokens = Budgeting.ContextBudgetEstimator.Estimate(compressed),
            Strategy = "RouteCompressor",
            SourceChunkIds = sourceChunkIds
        };
    }

    private static string InferLayer(GraphNode? node)
    {
        if (node is null) return "?";
        if (node.Attributes.ContainsKey("aspnet-route:entry-point") || node.Attributes.ContainsKey("route"))
            return "R";
        if (node.Kind == "entity") return "E";
        if (node.Kind == "table") return "T";
        var cn = node.ClassName ?? "";
        if (cn.EndsWith("Controller", StringComparison.Ordinal)) return "C";
        if (cn.EndsWith("Service", StringComparison.Ordinal)) return "S";
        if (cn.EndsWith("Repository", StringComparison.Ordinal) || cn.EndsWith("Dao", StringComparison.Ordinal)) return "P";
        return "M";
    }

    private static string ShortenId(string nodeId)
    {
        var parts = nodeId.Split("::");
        if (parts.Length >= 4)
        {
            var methodPart = parts[3];
            var dotIdx = methodPart.LastIndexOf('.');
            return dotIdx >= 0 ? methodPart[(dotIdx + 1)..] : methodPart;
        }
        return nodeId.Length > 40 ? nodeId[..37] + "..." : nodeId;
    }
}

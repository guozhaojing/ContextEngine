using System.Text.Json;
using Core.Export.Dtos;
using Core.Graph;
using Core.Graph.Query;

namespace Core.Export;

public sealed class GraphExportService
{
    private readonly GraphQueryService _query;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GraphExportService(GraphQueryService query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    // ═══════════════════════════════════════════════════════════════
    // Single-item projections
    // ═══════════════════════════════════════════════════════════════

    public ExportNode ProjectNode(GraphNode node, ProjectionMode mode)
    {
        var layer = LayerInference.InferNodeLayer(node);

        var includeAttrs = mode >= ProjectionMode.Visualization && node.Attributes.Count > 0
            ? new Dictionary<string, string>(node.Attributes)
            : null;

        var includeDetail = mode >= ProjectionMode.Detailed;

        return new ExportNode
        {
            Id = node.Id,
            Label = node.Label,
            Kind = node.Kind,
            Layer = layer,
            NodeType = LayerInference.GetNodeType(layer),
            ViewState = ViewNodeState.Normal,
            ProjectName = includeDetail ? node.ProjectName : null,
            Namespace = includeDetail ? node.Namespace : null,
            ClassName = includeDetail ? node.ClassName : null,
            MethodName = includeDetail ? node.MethodName : null,
            IsExternal = includeDetail && node.IsExternal,
            Attributes = includeAttrs
        };
    }

    public ExportEdge? ProjectEdge(
        string fromId,
        string toId,
        string kind,
        string label,
        int sequence,
        ProjectionMode mode)
    {
        var edgeId = $"{fromId}\u2192{toId}|{kind}";
        var edgeInfo = _query.GetEdgeInfo(fromId, toId);

        var layer = LayerInference.InferEdgeLayer(kind);
        var confidence = edgeInfo?.GetAttr("confidence") ?? null;
        if (string.IsNullOrEmpty(confidence))
            confidence = null;

        var isCompact = mode == ProjectionMode.Compact;

        return new ExportEdge
        {
            Id = edgeId,
            From = fromId,
            To = toId,
            Kind = kind,
            Layer = layer,
            Confidence = isCompact ? null : confidence,
            Label = isCompact || string.IsNullOrEmpty(label) ? null : label,
            Sequence = sequence
        };
    }

    public ExportPath ProjectPath(SemanticPath path, ProjectionMode mode)
    {
        var rootLayer = path.NodeIds.Count > 0
            ? GetNodeLayer(path.NodeIds[0])
            : null;

        var leafLayer = path.NodeIds.Count > 0
            ? GetNodeLayer(path.NodeIds[^1])
            : null;

        var edgeIds = new List<string>(path.EdgeKinds.Count);
        for (var i = 0; i < path.EdgeKinds.Count; i++)
        {
            if (i < path.NodeIds.Count - 1)
            {
                var from = path.NodeIds[i];
                var to = path.NodeIds[i + 1];
                edgeIds.Add($"{from}\u2192{to}|{path.EdgeKinds[i]}");
            }
        }

        var explanation = mode >= ProjectionMode.Visualization
            ? BuildExplanation(path)
            : Array.Empty<PathExplanationStep>();

        return new ExportPath
        {
            Id = path.PathId,
            Nodes = path.NodeIds,
            Edges = edgeIds,
            Summary = path.Summary,
            Explanation = explanation,
            Length = path.Length,
            RootId = path.RootId,
            LeafId = path.LeafId,
            RootLayer = rootLayer,
            LeafLayer = leafLayer
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Batch projections
    // ═══════════════════════════════════════════════════════════════

    public NodesExport ExportNodes(
        IEnumerable<SemanticPath> paths,
        ProjectionMode mode = ProjectionMode.Detailed)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            foreach (var nodeId in path.NodeIds)
                nodeIds.Add(nodeId);
        }

        var nodes = new List<ExportNode>(nodeIds.Count);
        foreach (var nodeId in nodeIds)
        {
            var node = _query.GetNode(nodeId);
            if (node is not null)
                nodes.Add(ProjectNode(node, mode));
        }

        return new NodesExport
        {
            Nodes = nodes,
            Mode = mode
        };
    }

    public EdgesExport ExportEdges(
        IEnumerable<SemanticPath> paths,
        ProjectionMode mode = ProjectionMode.Detailed)
    {
        var edgeLookup = new Dictionary<string, ExportEdge>(StringComparer.Ordinal);
        var edgeSequence = 0;

        foreach (var path in paths)
        {
            for (var i = 0; i < path.NodeIds.Count - 1; i++)
            {
                var from = path.NodeIds[i];
                var to = path.NodeIds[i + 1];
                var kind = i < path.EdgeKinds.Count ? path.EdgeKinds[i] : "call";
                var label = i < path.HopLabels.Count ? path.HopLabels[i] : "";

                var edgeId = $"{from}\u2192{to}|{kind}";
                if (edgeLookup.ContainsKey(edgeId))
                    continue;

                var edge = ProjectEdge(from, to, kind, label, edgeSequence++, mode);
                if (edge is not null)
                    edgeLookup[edgeId] = edge;
            }
        }

        return new EdgesExport
        {
            Edges = edgeLookup.Values.OrderBy(e => e.Sequence).ToList(),
            Mode = mode
        };
    }

    public PathsExport ExportPaths(
        IReadOnlyList<SemanticPath> paths,
        ProjectionMode mode = ProjectionMode.Visualization)
    {
        var exportPaths = new List<ExportPath>(paths.Count);
        var totalHops = 0;

        foreach (var path in paths)
        {
            exportPaths.Add(ProjectPath(path, mode));
            totalHops += path.Length;
        }

        return new PathsExport
        {
            Paths = exportPaths,
            TotalHops = totalHops,
            Mode = mode
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Visualization
    // ═══════════════════════════════════════════════════════════════

    public VisualizationExport ExportVisualization(
        IReadOnlyList<SemanticPath> tableImpactPaths,
        IReadOnlyList<SemanticPath> apiToDbPaths,
        IReadOnlyList<SemanticPath> entityCenterPaths,
        string graphName)
    {
        var views = new List<ExportView>();

        if (tableImpactPaths.Count > 0)
        {
            views.Add(new ExportView
            {
                ViewId = "tableImpact",
                ViewName = "Table Impact Analysis",
                Description = "哪些 API 会访问这张表?",
                Direction = "backward",
                EdgeKinds = new[] { "call", "nh:entity-access" },
                RootNodeIds = tableImpactPaths
                    .Select(p => p.RootId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                MaxDepth = 15
            });
        }

        if (apiToDbPaths.Count > 0)
        {
            views.Add(new ExportView
            {
                ViewId = "apiToDb",
                ViewName = "API \u2192 DB Path",
                Description = "Route → Controller → Service → Repository → Entity → Table",
                Direction = "forward",
                EdgeKinds = new[] { "call", "nh:entity-access" },
                RootNodeIds = apiToDbPaths
                    .Select(p => p.RootId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                MaxDepth = 15
            });
        }

        if (entityCenterPaths.Count > 0)
        {
            var centerNodeId = entityCenterPaths.First().RootId;
            views.Add(new ExportView
            {
                ViewId = "entityCenter",
                ViewName = "Entity Center View",
                Description = "谁在读写这个 Entity? 它映射到哪张表?",
                Direction = "both",
                EdgeKinds = new[] { "call", "nh:entity-access" },
                CenterNodeId = centerNodeId,
                Radius = 3
            });
        }

        if (tableImpactPaths.Count > 0 || apiToDbPaths.Count > 0 || entityCenterPaths.Count > 0)
        {
            views.Add(new ExportView
            {
                ViewId = "multiLayer",
                ViewName = "Multi-Layer Traversal",
                Description = "展示跨 Layer 的完整调用链路",
                Direction = "both",
                EdgeKinds = new[] { "call", "spring:implements", "spring:property-ref", "nh:entity-access" }
            });
        }

        var styleHints = new Dictionary<string, StyleHint>(StringComparer.Ordinal)
        {
            ["route"] = new() { Color = "#e74c3c", Icon = "globe" },
            ["controller"] = new() { Color = "#e67e22", Icon = "server" },
            ["service"] = new() { Color = "#2ecc71", Icon = "cog" },
            ["repository"] = new() { Color = "#3498db", Icon = "database" },
            ["entity"] = new() { Color = "#9b59b6", Icon = "cube" },
            ["table"] = new() { Color = "#1abc9c", Icon = "th" }
        };

        return new VisualizationExport
        {
            GraphName = graphName,
            Views = views,
            Layout = new LayerLayout
            {
                Direction = "TB",
                LayerOrder = LayerInference.DefaultLayerOrder,
                Spacing = new LayoutSpacing { Node = 150, Layer = 200 }
            },
            StyleHints = styleHints
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Save all
    // ═══════════════════════════════════════════════════════════════

    public async Task SaveAllAsync(
        IReadOnlyList<SemanticPath> paths,
        string outputDirectory,
        string graphName = "ContextEngine",
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var nodesExport = ExportNodes(paths, ProjectionMode.Visualization);
        var edgesExport = ExportEdges(paths, ProjectionMode.Visualization);
        var pathsExport = ExportPaths(paths, ProjectionMode.Visualization);
        var viz = ExportVisualization(paths, paths, paths, graphName);

        await WriteJsonAsync(Path.Combine(outputDirectory, "nodes.json"), nodesExport, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "edges.json"), edgesExport, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "paths.json"), pathsExport, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "visualization.json"), viz, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private string? GetNodeLayer(string nodeId)
    {
        var node = _query.GetNode(nodeId);
        return node is not null ? LayerInference.InferNodeLayer(node) : null;
    }

    private IReadOnlyList<PathExplanationStep> BuildExplanation(SemanticPath path)
    {
        var steps = new List<PathExplanationStep>(path.NodeIds.Count);

        for (var i = 0; i < path.NodeIds.Count; i++)
        {
            var nodeId = path.NodeIds[i];
            var node = _query.GetNode(nodeId);
            var layer = node is not null ? LayerInference.InferNodeLayer(node) : "unknown";

            var humanText = BuildHumanText(node, layer);

            if (i < path.EdgeKinds.Count && i < path.NodeIds.Count - 1)
            {
                humanText += $" → [{path.EdgeKinds[i]}] ";
            }

            steps.Add(new PathExplanationStep
            {
                NodeId = nodeId,
                Layer = layer,
                HumanText = humanText
            });
        }

        return steps;
    }

    private static string BuildHumanText(GraphNode? node, string layer)
    {
        if (node is null)
            return "Unknown";

        var name = node.Label;
        if (name.Length > 50)
            name = name[..47] + "...";

        return layer switch
        {
            "route" => $"Route [{name}]",
            "controller" => $"Controller [{name}]",
            "service" => $"Service [{name}]",
            "repository" => $"Repository [{name}]",
            "entity" => $"Entity [{name}]",
            "table" => $"Table [{name}]",
            _ => $"Method [{name}]"
        };
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T data,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}

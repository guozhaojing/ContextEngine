using System.Text.Json;
using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;

namespace App.Mcp;

public sealed class ContextEngineMcpTools
{
    private readonly GraphQueryService _query;
    private readonly McpToolDefinition[] _definitions;

    public ContextEngineMcpTools(GraphQueryService query)
    {
        _query = query;
        _definitions = BuildDefinitions();
    }

    public IReadOnlyList<McpToolDefinition> Definitions => _definitions;

    public object Invoke(string name, Dictionary<string, JsonElement> args)
    {
        return name switch
        {
            "ce_get_node" => GetNode(args),
            "ce_search_nodes" => SearchNodes(args),
            "ce_get_callers" => GetCallers(args),
            "ce_get_callees" => GetCallees(args),
            "ce_get_call_chain" => GetCallChain(args),
            "ce_find_entry_points" => FindEntryPoints(args),
            "ce_find_impact" => FindImpact(args),
            "ce_find_table_impact" => FindTableImpact(args),
            "ce_find_routes_to_table" => FindRoutesToTable(args),
            "ce_list_entry_points" => ListEntryPoints(),
            "ce_get_edges" => GetEdges(args),
            "ce_get_stats" => GetStats(),
            "ce_find_semantic_path" => FindSemanticPath(args),
            _ => throw new KeyNotFoundException($"Unknown tool: {name}"),
        };
    }

    // ── Tool implementations ──

    private object GetNode(Dictionary<string, JsonElement> args)
    {
        var id = GetStringArg(args, "methodId");
        var node = _query.GetNode(id);
        if (node is null) return new { error = $"Node not found: {id}" };

        return new
        {
            id = node.Id,
            label = node.Label,
            kind = node.Kind,
            className = node.ClassName,
            namespaceName = node.Namespace,
            sourceFile = node.SourceFile,
            symbolHandle = node.SymbolHandle,
            groundingKind = node.GroundingKind,
            truthType = node.TruthType,
            confidence = node.Confidence,
            isExternal = node.IsExternal,
            callerCount = _query.GetCallers(id).Count,
            calleeCount = _query.GetCallees(id).Count,
        };
    }

    private object SearchNodes(Dictionary<string, JsonElement> args)
    {
        var query = GetStringArg(args, "query").ToLowerInvariant();
        var kind = GetOptionalStringArg(args, "kind");
        var limit = GetOptionalIntArg(args, "limit", 20);

        var results = _query.GetAllNodes()
            .Where(n =>
            {
                var label = n.Label.ToLowerInvariant();
                var cls = n.ClassName.ToLowerInvariant();
                var ns = n.Namespace.ToLowerInvariant();
                return label.Contains(query, StringComparison.Ordinal)
                    || cls.Contains(query, StringComparison.Ordinal)
                    || ns.Contains(query, StringComparison.Ordinal);
            })
            .Where(n => kind is null || n.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(n => new
            {
                id = n.Id,
                label = n.Label,
                kind = n.Kind,
                className = n.ClassName,
                namespaceName = n.Namespace,
                sourceFile = n.SourceFile,
                confidence = n.Confidence,
            })
            .ToList();

        return new { results, total = results.Count };
    }

    private object GetCallers(Dictionary<string, JsonElement> args)
    {
        var id = GetStringArg(args, "methodId");
        var callers = _query.GetCallers(id);
        var nodes = callers.Select(cid => NodeSummary(cid)).ToList();
        return new { methodId = id, callers = nodes, total = nodes.Count };
    }

    private object GetCallees(Dictionary<string, JsonElement> args)
    {
        var id = GetStringArg(args, "methodId");
        var callees = _query.GetCallees(id);
        var nodes = callees.Select(cid => NodeSummary(cid)).ToList();
        return new { methodId = id, callees = nodes, total = nodes.Count };
    }

    private object GetCallChain(Dictionary<string, JsonElement> args)
    {
        var id = GetStringArg(args, "methodId");
        var depth = GetOptionalIntArg(args, "depth", 3);
        var chains = _query.GetCallChain(id, depth);
        var enriched = chains.Select(chain =>
            chain.Select(cid => new { id = cid, label = _query.GetNode(cid)?.Label ?? cid }).ToList()
        ).ToList();
        return new { methodId = id, depth, chains = enriched, total = chains.Count };
    }

    private object FindEntryPoints(Dictionary<string, JsonElement> args)
    {
        var id = GetStringArg(args, "methodId");
        var entries = _query.FindEntryPoints(id);
        var nodes = entries.Select(eid => NodeSummary(eid)).ToList();
        return new { methodId = id, entryPoints = nodes, total = nodes.Count };
    }

    private object FindImpact(Dictionary<string, JsonElement> args)
    {
        var id = GetStringArg(args, "methodId");
        var paths = _query.FindImpactByMethod(id);
        var result = paths.Select(p => new
        {
            pathId = p.PathId,
            nodeIds = p.NodeIds,
            labels = p.NodeIds.Select(nid => _query.GetNode(nid)?.Label ?? nid).ToList(),
            edgeKinds = p.EdgeKinds,
            depth = p.Length,
            summary = p.Summary,
        }).ToList();
        return new { methodId = id, paths = result, total = result.Count };
    }

    private object FindTableImpact(Dictionary<string, JsonElement> args)
    {
        var table = GetStringArg(args, "tableName");
        var paths = _query.FindTableImpact(table);
        var result = paths.Select(p => new
        {
            pathId = p.PathId,
            nodeIds = p.NodeIds,
            labels = p.NodeIds.Select(nid => _query.GetNode(nid)?.Label ?? nid).ToList(),
            edgeKinds = p.EdgeKinds,
            depth = p.Length,
            summary = p.Summary,
        }).ToList();
        return new { tableName = table, paths = result, total = result.Count };
    }

    private object FindRoutesToTable(Dictionary<string, JsonElement> args)
    {
        var table = GetStringArg(args, "tableName");
        var paths = _query.FindRoutesToTable(table);
        var result = paths.Select(p => new
        {
            pathId = p.PathId,
            nodeIds = p.NodeIds,
            labels = p.NodeIds.Select(nid => _query.GetNode(nid)?.Label ?? nid).ToList(),
            edgeKinds = p.EdgeKinds,
            depth = p.Length,
            summary = p.Summary,
        }).ToList();
        return new { tableName = table, routes = result, total = result.Count };
    }

    private object ListEntryPoints()
    {
        var entries = _query.FindEntryPointNodes();
        var nodes = entries.Select(eid => NodeSummary(eid)).ToList();
        return new { entryPoints = nodes, total = nodes.Count };
    }

    private object GetEdges(Dictionary<string, JsonElement> args)
    {
        var id = GetStringArg(args, "methodId");
        var direction = GetOptionalStringArg(args, "direction", "both");

        List<EdgeInfo> edges = new();
        if (direction == "out" || direction == "both")
            edges.AddRange(_query.GetOutgoingEdges(id));
        if (direction == "in" || direction == "both")
            edges.AddRange(_query.GetIncomingEdges(id));

        var result = edges.Select(e => new
        {
            toId = e.ToId,
            toLabel = _query.GetNode(e.ToId)?.Label ?? e.ToId,
            kind = e.Kind,
            label = e.Label,
            isResolved = e.IsResolved,
            confidence = e.Confidence,
            evidence = e.Evidence,
            grounded = e.Grounded,
        }).ToList();

        return new { nodeId = id, direction, edges = result, total = result.Count };
    }

    private object GetStats()
    {
        var nodes = _query.GetAllNodes().ToList();
        var kindCounts = nodes.GroupBy(n => n.Kind)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());

        return new
        {
            totalNodes = nodes.Count,
            totalEntryPoints = _query.FindEntryPointNodes().Count,
            byKind = kindCounts,
        };
    }

    private object FindSemanticPath(Dictionary<string, JsonElement> args)
    {
        var fromId = GetStringArg(args, "fromId");
        var toId = GetStringArg(args, "toId");
        var maxDepth = GetOptionalIntArg(args, "maxDepth", 15);

        var options = new SemanticTraversalOptions
        {
            EdgeKinds = new HashSet<string>(StringComparer.Ordinal)
                { "call", "spring:implements", "spring:property-ref", "nh:entity-access" },
            Direction = TraversalDirection.Forward,
            MaxDepth = maxDepth,
            MaxPaths = 50,
            DeduplicatePaths = true,
        };

        var paths = _query.FindSemanticPath(fromId, toId, options);
        var result = paths.Select(p => new
        {
            pathId = p.PathId,
            nodeIds = p.NodeIds,
            labels = p.NodeIds.Select(nid => _query.GetNode(nid)?.Label ?? nid).ToList(),
            edgeKinds = p.EdgeKinds,
            depth = p.Length,
            summary = p.Summary,
        }).ToList();

        return new { fromId, toId, paths = result, total = result.Count };
    }

    // ── Helpers ──

    private object NodeSummary(string id)
    {
        var node = _query.GetNode(id);
        if (node is null) return new { id, label = id };
        return new
        {
            id = node.Id,
            label = node.Label,
            kind = node.Kind,
            className = node.ClassName,
            namespaceName = node.Namespace,
            sourceFile = node.SourceFile,
            confidence = node.Confidence,
        };
    }

    private static string GetStringArg(Dictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            throw new ArgumentException($"Missing required parameter: {key}");
        return el.GetString() ?? "";
    }

    private static string? GetOptionalStringArg(Dictionary<string, JsonElement> args, string key, string? fallback = null)
    {
        if (!args.TryGetValue(key, out var el)) return fallback;
        return el.GetString() ?? fallback;
    }

    private static int GetOptionalIntArg(Dictionary<string, JsonElement> args, string key, int fallback)
    {
        if (!args.TryGetValue(key, out var el)) return fallback;
        return el.TryGetInt32(out var v) ? v : fallback;
    }

    // ── Tool definitions ──

    private static McpToolDefinition[] BuildDefinitions() => new McpToolDefinition[]
    {
        new("ce_get_node", "Get details of a graph node by its method ID. Use this to inspect a specific method, entity, or table node.")
        {
            Parameters = { new("methodId", "string", true, "Stable method ID, e.g. Project.Class.Method(int,string)") },
        },
        new("ce_search_nodes", "Search graph nodes by name, class, namespace, or kind. Use this to discover method IDs when you only know part of a name.")
        {
            Parameters =
            {
                new("query", "string", true, "Search term; matched against label, class name, and namespace (case-insensitive)"),
                new("kind", "string", false, "Optional node kind filter, e.g. method, entity, table"),
                new("limit", "number", false, "Max results (default 20)"),
            },
        },
        new("ce_get_callers", "Get all methods that call the given method (upstream dependencies). Use this to find what depends on a method.")
        {
            Parameters = { new("methodId", "string", true, "Target method ID") },
        },
        new("ce_get_callees", "Get all methods called by the given method (downstream dependencies). Use this to understand what a method does internally.")
        {
            Parameters = { new("methodId", "string", true, "Source method ID") },
        },
        new("ce_get_call_chain", "Expand the downstream call chain from a method up to a given depth. Returns multiple paths when branching occurs.")
        {
            Parameters =
            {
                new("methodId", "string", true, "Starting method ID"),
                new("depth", "number", false, "Number of edges to follow (default 3)"),
            },
        },
        new("ce_find_entry_points", "Trace upstream from a method to all HTTP entry points (ASP.NET routes) that eventually reach it.")
        {
            Parameters = { new("methodId", "string", true, "Method ID to trace from") },
        },
        new("ce_find_impact", "Full bidirectional impact analysis for a method: upstream callers plus downstream data access. Shows what would break if this method changes.")
        {
            Parameters = { new("methodId", "string", true, "Method ID to analyze") },
        },
        new("ce_find_table_impact", "Given a database table name, find all API entry points that are affected when the table changes. Traces Table → Entity → Repository → Service → Controller → Route.")
        {
            Parameters = { new("tableName", "string", true, "Database table name") },
        },
        new("ce_find_routes_to_table", "Given a database table name, trace all API routes down to the table access. Returns Route → Controller → Service → Repository → Entity → Table paths.")
        {
            Parameters = { new("tableName", "string", true, "Database table name") },
        },
        new("ce_list_entry_points", "List all HTTP API entry points in the code graph. Useful for discovering available endpoints.")
        {
            Parameters = { },
        },
        new("ce_get_edges", "Get incoming/outgoing edges for a node. Reveals the relationship types (call, spring:implements, nh:entity-access, etc.)")
        {
            Parameters =
            {
                new("methodId", "string", true, "Node ID"),
                new("direction", "string", false, "in, out, or both (default both)"),
            },
        },
        new("ce_get_stats", "Get overall code graph statistics: total nodes, entry points, and node counts by kind.")
        {
            Parameters = { },
        },
        new("ce_find_semantic_path", "Find multi-hop semantic paths between two nodes. Uses call, Spring, and NHibernate edges.")
        {
            Parameters =
            {
                new("fromId", "string", true, "Source node ID"),
                new("toId", "string", true, "Target node ID"),
                new("maxDepth", "number", false, "Max hops (default 15)"),
            },
        },
    };
}

public sealed class McpToolDefinition
{
    public string Name { get; }
    public string Description { get; }
    public List<McpToolParam> Parameters { get; } = new();

    public McpToolDefinition(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

public sealed class McpToolParam
{
    public string Name { get; }
    public string Type { get; }
    public bool Required { get; }
    public string Description { get; }

    public McpToolParam(string name, string type, bool required, string description)
    {
        Name = name;
        Type = type;
        Required = required;
        Description = description;
    }
}

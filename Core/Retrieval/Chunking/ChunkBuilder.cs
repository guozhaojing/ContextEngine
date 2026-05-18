using System.Text;
using Core.Graph;
using Core.Graph.Query;

namespace Core.Retrieval.Chunking;

public sealed class ChunkBuilder
{
    private readonly GraphQueryService _query;

    public ChunkBuilder(GraphQueryService query)
    {
        _query = query;
    }

    // ═══════════════════════════════════════════════════════════════
    // Build all chunks
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<CodeChunk> BuildAll()
    {
        var chunks = new List<CodeChunk>();

        chunks.AddRange(BuildMethodChunks());
        chunks.AddRange(BuildClassChunks());
        chunks.AddRange(BuildSemanticPathChunks());
        chunks.AddRange(BuildEntityAccessChunks());
        chunks.AddRange(BuildRouteChunks());

        return chunks;
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. Method chunks — one chunk per non-external method
    // ═══════════════════════════════════════════════════════════════

    private IEnumerable<CodeChunk> BuildMethodChunks()
    {
        var nodes = _query.GetAllNodes()
            .Where(n => !n.IsExternal && n.Kind == GraphNodeKind.Method)
            .ToList();

        foreach (var node in nodes)
        {
            var content = BuildMethodContent(node);
            var keywords = ExtractKeywords(node);
            var entityInfo = ExtractEntityInfo(node);
            var routeInfo = ExtractRouteInfo(node);

            yield return new CodeChunk
            {
                ChunkId = $"method:{node.Id}",
                Kind = ChunkKind.Method,
                Title = node.Label,
                Summary = BuildMethodSummary(node),
                Content = content,
                Keywords = keywords,
                NodeIds = new[] { node.Id },
                EdgeKinds = GetEdgeKindsForNode(node.Id),
                EntryPoints = routeInfo.EntryPoints,
                EntityNames = entityInfo.EntityNames,
                TableNames = entityInfo.TableNames,
                RoutePatterns = routeInfo.RoutePatterns,
                SourceFiles = new[] { node.ProjectName ?? node.ProjectPath },
                ImportanceScore = ComputeImportance(ChunkKind.Method, node, entityInfo.HasData, routeInfo.HasData),
                TokenEstimate = EstimateTokens(content, keywords)
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. Class chunks — group methods by (namespace, class)
    // ═══════════════════════════════════════════════════════════════

    private IEnumerable<CodeChunk> BuildClassChunks()
    {
        var methodNodes = _query.GetAllNodes()
            .Where(n => !n.IsExternal && n.Kind == GraphNodeKind.Method
                        && !string.IsNullOrEmpty(n.ClassName))
            .ToList();

        var groups = methodNodes
            .GroupBy(n => (n.Namespace, n.ClassName, n.ProjectName));

        foreach (var group in groups)
        {
            if (group.Count() < 2) continue; // skip single-method "classes"

            var classKey = string.IsNullOrEmpty(group.Key.Namespace)
                ? group.Key.ClassName
                : $"{group.Key.Namespace}.{group.Key.ClassName}";

            var members = group.ToList();
            var allKeywords = members.SelectMany(ExtractKeywords).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var allNodeIds = members.Select(n => n.Id).ToList();
            var content = BuildClassContent(classKey, members);
            var entityInfo = ExtractEntityInfoFromNodes(members);

            yield return new CodeChunk
            {
                ChunkId = $"class:{group.Key.ProjectName}::{classKey}",
                Kind = ChunkKind.Class,
                Title = classKey,
                Summary = $"{classKey}: {members.Count} methods in {group.Key.ProjectName}",
                Content = content,
                Keywords = allKeywords,
                NodeIds = allNodeIds,
                EdgeKinds = members.SelectMany(n => GetEdgeKindsForNode(n.Id)).Distinct().ToList(),
                EntryPoints = members.Where(n => IsEntryPoint(n)).Select(n => n.Label).ToList(),
                EntityNames = entityInfo.EntityNames,
                TableNames = entityInfo.TableNames,
                RoutePatterns = members.SelectMany(n => ExtractRouteInfo(n).RoutePatterns).Distinct().ToList(),
                SourceFiles = new[] { group.Key.ProjectName ?? "" },
                ImportanceScore = ComputeClassImportance(members),
                TokenEstimate = EstimateTokens(content, allKeywords)
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. Semantic path chunks — Route→Table or Table→Route paths
    // ═══════════════════════════════════════════════════════════════

    private IEnumerable<CodeChunk> BuildSemanticPathChunks()
    {
        var tableNames = GetAvailableTableNames();
        var builtSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in tableNames)
        {
            // Forward: Route → Table
            var fwdPaths = _query.FindRoutesToTable(table);
            foreach (var path in fwdPaths)
            {
                if (!builtSet.Add(path.PathId)) continue;

                yield return BuildPathChunk(path, table);
            }

            // Backward: Table → Route
            var bwdPaths = _query.FindTableImpact(table);
            foreach (var path in bwdPaths)
            {
                if (!builtSet.Add(path.PathId)) continue;

                yield return BuildPathChunk(path, table);
            }
        }
    }

    private CodeChunk BuildPathChunk(SemanticPath path, string tableName)
    {
        var content = new StringBuilder();
        content.AppendLine($"## Semantic Path: {path.RootId} → {path.LeafId}");
        content.AppendLine();
        content.AppendLine($"**Length**: {path.Length} hops");
        content.AppendLine($"**Table**: {tableName}");

        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entityNames = new HashSet<string>(StringComparer.Ordinal);
        var tableNames = new HashSet<string>(StringComparer.Ordinal) { tableName };
        var routePatterns = new HashSet<string>(StringComparer.Ordinal);

        content.AppendLine();
        content.AppendLine("### Path Steps");
        for (var i = 0; i < path.NodeIds.Count; i++)
        {
            var node = _query.GetNode(path.NodeIds[i]);
            var label = node?.Label ?? path.NodeIds[i];
            var kind = i < path.EdgeKinds.Count ? path.EdgeKinds[i] : "end";
            content.AppendLine($"  {i + 1}. {label}");

            if (node is not null)
            {
                ExtractKeywordsFromLabel(label, keywords);
                var route = node.Attributes.GetValueOrDefault("route", "");
                if (!string.IsNullOrEmpty(route)) routePatterns.Add(route);
                if (node.Kind == GraphNodeKind.Entity) entityNames.Add(node.ClassName ?? label);
            }
        }

        return new CodeChunk
        {
            ChunkId = $"path:{path.PathId}",
            Kind = ChunkKind.SemanticPath,
            Title = $"[{path.Length}h] {path.RootId} → {path.LeafId}",
            Summary = path.Summary,
            Content = content.ToString(),
            Keywords = keywords.ToList(),
            NodeIds = path.NodeIds,
            EdgeKinds = path.EdgeKinds,
            EntryPoints = routePatterns.ToList(),
            EntityNames = entityNames.ToList(),
            TableNames = tableNames.ToList(),
            RoutePatterns = routePatterns.ToList(),
            SourceFiles = GetSourceFilesForNodes(path.NodeIds),
            ImportanceScore = 6.0 + Math.Min(path.Length * 0.5, 4.0),
            TokenEstimate = EstimateTokens(content.ToString(), keywords)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. Entity access chunks — per entity, all accessing methods
    // ═══════════════════════════════════════════════════════════════

    private IEnumerable<CodeChunk> BuildEntityAccessChunks()
    {
        var entityNodes = _query.GetAllNodes()
            .Where(n => n.Kind == GraphNodeKind.Entity)
            .ToList();

        foreach (var entityNode in entityNodes)
        {
            var entityClass = entityNode.ClassName ?? ParseEntityClass(entityNode.Id);
            if (string.IsNullOrEmpty(entityClass)) continue;

            var tableName = entityNode.Attributes.GetValueOrDefault("nh:table", "")
                            ?? ParseTableName(entityNode.Id);
            if (string.IsNullOrEmpty(tableName)) tableName = entityClass;

            // Find all paths that access this entity
            var paths = _query.FindApisByEntity(entityClass);
            if (paths.Count == 0) continue;

            var allNodeIds = new HashSet<string>(StringComparer.Ordinal);
            var allKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entryPoints = new HashSet<string>(StringComparer.Ordinal);

            foreach (var p in paths)
            {
                foreach (var nid in p.NodeIds) allNodeIds.Add(nid);
                foreach (var nid in p.NodeIds)
                {
                    var node = _query.GetNode(nid);
                    if (node is not null)
                    {
                        ExtractKeywordsFromLabel(node.Label, allKeywords);
                        if (IsEntryPoint(node)) entryPoints.Add(node.Label);
                    }
                }
            }

            var content = new StringBuilder();
            content.AppendLine($"## Entity: {entityClass}");
            content.AppendLine($"**Table**: {tableName}");
            content.AppendLine($"**Access Methods**: {allNodeIds.Count} nodes across {paths.Count} paths");
            content.AppendLine();

            foreach (var p in paths.Take(10))
            {
                content.AppendLine($"### {p.Summary}");
            }

            yield return new CodeChunk
            {
                ChunkId = $"entity:{entityClass}",
                Kind = ChunkKind.EntityAccess,
                Title = $"Entity: {entityClass} → {tableName}",
                Summary = $"{entityClass} accessed by {paths.Count} API paths, mapped to table {tableName}",
                Content = content.ToString(),
                Keywords = allKeywords.ToList(),
                NodeIds = allNodeIds.ToList(),
                EdgeKinds = paths.SelectMany(p => p.EdgeKinds).Distinct().ToList(),
                EntryPoints = entryPoints.ToList(),
                EntityNames = new[] { entityClass },
                TableNames = new[] { tableName },
                RoutePatterns = entryPoints.ToList(),
                SourceFiles = GetSourceFilesForNodes(allNodeIds),
                ImportanceScore = 7.0 + Math.Min(paths.Count * 0.5, 3.0),
                TokenEstimate = EstimateTokens(content.ToString(), allKeywords)
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. Route chunks — per API entry point
    // ═══════════════════════════════════════════════════════════════

    private IEnumerable<CodeChunk> BuildRouteChunks()
    {
        var entryNodeIds = _query.FindEntryPointNodes();
        if (entryNodeIds.Count == 0) yield break;

        foreach (var entryId in entryNodeIds)
        {
            var node = _query.GetNode(entryId);
            if (node is null) continue;

            var route = node.Attributes.GetValueOrDefault("route", "");
            var httpMethod = node.Attributes.GetValueOrDefault("http-method", "");

            // Forward traversal to find what this route calls
            var paths = _query.FindImpactByMethod(entryId);
            if (paths.Count == 0) continue;

            var allNodeIds = new HashSet<string>(StringComparer.Ordinal) { entryId };
            var allKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExtractKeywordsFromLabel(node.Label, allKeywords);

            foreach (var p in paths)
            {
                foreach (var nid in p.NodeIds) allNodeIds.Add(nid);
            }

            var content = new StringBuilder();
            content.AppendLine($"## Route: {(string.IsNullOrEmpty(httpMethod) ? "" : httpMethod + " ")}{route}");
            content.AppendLine($"**Entry Point**: {node.Label}");
            content.AppendLine($"**Class**: {node.ClassName}");

            var entityInfo = ExtractEntityInfo(node);
            if (entityInfo.TableNames.Count > 0)
                content.AppendLine($"**Tables**: {string.Join(", ", entityInfo.TableNames)}");

            content.AppendLine();
            content.AppendLine("### Call Chains");
            foreach (var p in paths.Take(8))
                content.AppendLine($"  {p.Summary}");

            yield return new CodeChunk
            {
                ChunkId = $"route:{entryId}",
                Kind = ChunkKind.Route,
                Title = $"{(string.IsNullOrEmpty(httpMethod) ? "" : httpMethod + " ")}{(string.IsNullOrEmpty(route) ? node.Label : route)}",
                Summary = $"API endpoint {(string.IsNullOrEmpty(httpMethod) ? "" : httpMethod + " ")}{route} → {paths.Count} call chains",
                Content = content.ToString(),
                Keywords = allKeywords.ToList(),
                NodeIds = allNodeIds.ToList(),
                EdgeKinds = paths.SelectMany(p => p.EdgeKinds).Distinct().ToList(),
                EntryPoints = new[] { node.Label },
                EntityNames = entityInfo.EntityNames,
                TableNames = entityInfo.TableNames,
                RoutePatterns = string.IsNullOrEmpty(route) ? Array.Empty<string>() : new[] { route },
                SourceFiles = new[] { node.ProjectName ?? node.ProjectPath },
                ImportanceScore = 8.0 + Math.Min(paths.Count * 0.3, 2.0),
                TokenEstimate = EstimateTokens(content.ToString(), allKeywords)
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Content builders
    // ═══════════════════════════════════════════════════════════════

    private string BuildMethodContent(GraphNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {node.Label}");
        sb.AppendLine();
        sb.AppendLine($"- **Namespace**: {node.Namespace}");
        sb.AppendLine($"- **Class**: {node.ClassName}");
        sb.AppendLine($"- **Method**: {node.MethodName}");
        sb.AppendLine($"- **Kind**: {node.Kind}");
        sb.AppendLine($"- **Project**: {node.ProjectName}");

        if (node.ParameterTypes.Count > 0)
            sb.AppendLine($"- **Parameters**: {string.Join(", ", node.ParameterTypes)}");

        var callees = _query.GetCallees(node.Id);
        if (callees.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Calls");
            foreach (var cid in callees.Take(20))
            {
                var callee = _query.GetNode(cid);
                sb.AppendLine($"  → {(callee?.Label ?? cid)}");
            }
        }

        var callers = _query.GetCallers(node.Id);
        if (callers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Called By");
            foreach (var cid in callers.Take(10))
            {
                var caller = _query.GetNode(cid);
                sb.AppendLine($"  ← {(caller?.Label ?? cid)}");
            }
        }

        var attrs = node.Attributes.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToList();
        if (attrs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Attributes");
            foreach (var (k, v) in attrs)
                sb.AppendLine($"  - {k} = {v}");
        }

        return sb.ToString();
    }

    private static string BuildClassContent(string classKey, List<GraphNode> members)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Class: {classKey}");
        sb.AppendLine($"**Methods**: {members.Count}");
        sb.AppendLine();

        sb.AppendLine("### Members");
        foreach (var m in members)
            sb.AppendLine($"  - {m.MethodName}: {m.Label}");

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // Keyword extraction
    // ═══════════════════════════════════════════════════════════════

    private static IReadOnlyList<string> ExtractKeywords(GraphNode node)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ExtractKeywordsFromLabel(node.Label, words);
        ExtractKeywordsFromLabel(node.ClassName, words);
        ExtractKeywordsFromLabel(node.MethodName, words);
        ExtractKeywordsFromLabel(node.Namespace, words);

        // Route keywords
        if (node.Attributes.TryGetValue("route", out var route) && !string.IsNullOrEmpty(route))
        {
            words.Add("api");
            words.Add("endpoint");
            foreach (var seg in route.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!seg.StartsWith('{')) words.Add(seg);
            }
        }

        if (node.Attributes.TryGetValue("http-method", out var method) && !string.IsNullOrEmpty(method))
            words.Add(method.ToLowerInvariant());

        // Entity keywords
        if (node.Attributes.TryGetValue("nh:table", out var table) && !string.IsNullOrEmpty(table))
            words.Add(table);

        // Spring
        if (node.Attributes.TryGetValue("spring-bean", out _))
            words.Add("bean");

        return words.Where(w => w.Length > 1).ToList();
    }

    private static void ExtractKeywordsFromLabel(string? label, HashSet<string> words)
    {
        if (string.IsNullOrEmpty(label)) return;

        // Split PascalCase/camelCase
        foreach (var word in SplitCamelCase(label))
        {
            if (word.Length > 1)
                words.Add(word.ToLowerInvariant());
        }

        // Also try splitting by common delimiters
        foreach (var part in label.Split('.', '_', '-', '/'))
        {
            if (part.Length > 1 && !part.StartsWith('{'))
                words.Add(part.ToLowerInvariant());
            foreach (var word in SplitCamelCase(part))
            {
                if (word.Length > 1)
                    words.Add(word.ToLowerInvariant());
            }
        }
    }

    private static IEnumerable<string> SplitCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input)) yield break;

        var start = 0;
        for (var i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]) && (char.IsLower(input[i - 1]) || (i + 1 < input.Length && char.IsLower(input[i + 1]))))
            {
                yield return input[start..i];
                start = i;
            }
        }

        yield return input[start..];
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static string BuildMethodSummary(GraphNode node)
    {
        var parts = new List<string> { node.Label };
        if (!string.IsNullOrEmpty(node.ClassName)) parts.Add(node.ClassName);
        return string.Join(" — ", parts);
    }

    private static bool IsEntryPoint(GraphNode node)
    {
        return node.Attributes.ContainsKey("aspnet-route:entry-point")
            || node.Attributes.ContainsKey("route");
    }

    private IReadOnlyList<string> GetEdgeKindsForNode(string nodeId)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var calleeId in _query.GetCallees(nodeId))
        {
            var edge = _query.GetEdgeInfo(nodeId, calleeId);
            if (edge is not null && !string.IsNullOrEmpty(edge.Value.Kind))
                kinds.Add(edge.Value.Kind);
        }
        return kinds.ToList();
    }

    private (bool HasData, List<string> EntityNames, List<string> TableNames) ExtractEntityInfo(GraphNode node)
    {
        var entityNames = new List<string>();
        var tableNames = new List<string>();

        if (node.Attributes.TryGetValue("nh:entity-class", out var ec) && !string.IsNullOrEmpty(ec))
            entityNames.Add(ec);
        if (node.Attributes.TryGetValue("nh:table", out var tb) && !string.IsNullOrEmpty(tb))
            tableNames.Add(tb);

        return (entityNames.Count > 0 || tableNames.Count > 0, entityNames, tableNames);
    }

    private static (bool HasData, List<string> RoutePatterns, List<string> EntryPoints) ExtractRouteInfo(GraphNode node)
    {
        var patterns = new List<string>();
        var entryPoints = new List<string>();

        if (node.Attributes.TryGetValue("route", out var route) && !string.IsNullOrEmpty(route))
            patterns.Add(route);
        if (IsEntryPoint(node))
            entryPoints.Add(node.Label);

        return (patterns.Count > 0 || entryPoints.Count > 0, patterns, entryPoints);
    }

    private static (List<string> EntityNames, List<string> TableNames) ExtractEntityInfoFromNodes(List<GraphNode> nodes)
    {
        var entityNames = new HashSet<string>(StringComparer.Ordinal);
        var tableNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var info = node.Kind == GraphNodeKind.Entity ? ExtractEntityInfoForEntityNode(node) : default;
            if (info.EntityName is not null) entityNames.Add(info.EntityName);
            if (info.TableName is not null) tableNames.Add(info.TableName);

            if (node.Attributes.TryGetValue("nh:entity-class", out var ec) && !string.IsNullOrEmpty(ec))
                entityNames.Add(ec);
            if (node.Attributes.TryGetValue("nh:table", out var tb) && !string.IsNullOrEmpty(tb))
                tableNames.Add(tb);
        }

        return (entityNames.ToList(), tableNames.ToList());
    }

    private static (string? EntityName, string? TableName) ExtractEntityInfoForEntityNode(GraphNode node)
    {
        var id = node.Id;
        var parts = id.Split("::");
        if (parts.Length >= 4)
            return (parts[3], parts.Length >= 5 ? parts[4] : null);
        return (null, null);
    }

    private List<string> GetAvailableTableNames()
    {
        return _query.GetAllNodes()
            .Where(n => n.Kind == GraphNodeKind.Entity)
            .Select(n => n.Attributes.GetValueOrDefault("nh:table", ""))
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToList();
    }

    private List<string> GetSourceFilesForNodes(IEnumerable<string> nodeIds)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nid in nodeIds)
        {
            var node = _query.GetNode(nid);
            if (node is not null && !string.IsNullOrEmpty(node.ProjectPath))
                files.Add(node.ProjectPath);
            else if (node is not null && !string.IsNullOrEmpty(node.ProjectName))
                files.Add(node.ProjectName);
        }
        return files.ToList();
    }

    private static string ParseEntityClass(string nodeId)
    {
        var parts = nodeId.Split("::");
        if (parts.Length >= 4)
        {
            var entityPart = parts[3];
            var dotIdx = entityPart.LastIndexOf('.');
            return dotIdx >= 0 ? entityPart[(dotIdx + 1)..] : entityPart;
        }
        return "";
    }

    private static string ParseTableName(string nodeId)
    {
        var parts = nodeId.Split("::");
        return parts.Length >= 5 ? parts[4] : "";
    }

    // ═══════════════════════════════════════════════════════════════
    // Scoring
    // ═══════════════════════════════════════════════════════════════

    private double ComputeImportance(ChunkKind kind, GraphNode node, bool hasEntity, bool hasRoute)
    {
        double baseScore = kind switch
        {
            ChunkKind.Method => 1.0,
            ChunkKind.Class => 2.0,
            ChunkKind.SemanticPath => 4.0,
            ChunkKind.EntityAccess => 4.0,
            ChunkKind.Route => 5.0,
            _ => 1.0
        };

        if (hasEntity) baseScore += 2.0;
        if (hasRoute) baseScore += 3.0;
        if (IsEntryPoint(node)) baseScore += 2.0;

        var callerCount = _query.GetCallers(node.Id).Count;
        baseScore += Math.Min(callerCount * 0.5, 3.0);

        return Math.Min(baseScore, 10.0);
    }

    private static double ComputeClassImportance(List<GraphNode> members)
    {
        var score = 2.0;
        score += members.Count * 0.3;
        if (members.Any(IsEntryPoint)) score += 3.0;
        if (members.Any(n => n.Attributes.ContainsKey("nh:entity-access"))) score += 2.0;
        return Math.Min(score, 10.0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Token estimation
    // ═══════════════════════════════════════════════════════════════

    private static int EstimateTokens(string content, IReadOnlyCollection<string> keywords)
    {
        var wordCount = content.Split(' ', '\n', '\r', '\t')
            .Count(w => !string.IsNullOrWhiteSpace(w));
        var keywordTokens = keywords.Count * 3;
        return wordCount + keywordTokens;
    }
}

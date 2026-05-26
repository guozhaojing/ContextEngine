// =============================================================================
// Cognition/CodeFix/SymbolLocator.cs — find target method in graph
// =============================================================================
// v2: semantic search via SemanticEmbeddingService when available;
//     falls back to keyword matching when index not built.
// =============================================================================
using Core.Cognition.SemanticDoc;
using Core.Graph;

namespace Core.Cognition.CodeFix;

public sealed class SymbolLocator
{
    private readonly GraphQueryService _graphQuery;
    private readonly SemanticEmbeddingService? _semanticSearch;

    public SymbolLocator(GraphQueryService graphQuery, SemanticEmbeddingService? semanticSearch = null)
    {
        _graphQuery = graphQuery;
        _semanticSearch = semanticSearch;
    }

    public List<LocatedSymbol> Locate(CodeFixRequest request)
    {
        var results = new List<LocatedSymbol>();

        // Try exact method name match first (fast path)
        if (!string.IsNullOrWhiteSpace(request.TargetMethodName))
        {
            var exactMatches = _graphQuery.GetAllNodes()
                .Where(n => !n.IsExternal && !string.IsNullOrEmpty(n.SourceFile)
                    && string.Equals(n.MethodName, request.TargetMethodName, StringComparison.Ordinal))
                .OrderBy(n => n.Label, StringComparer.Ordinal)
                .Take(10)
                .ToList();

            foreach (var node in exactMatches)
            {
                var symbol = BuildSymbol(node);
                if (symbol is not null) results.Add(symbol);
            }

            if (results.Count > 0) return results;
        }

        // File + method name match
        if (request.TargetFilePath is not null && request.TargetMethodName is not null)
        {
            var match = FindMethod(request.TargetFilePath, request.TargetMethodName);
            if (match is not null) results.Add(match);
            if (results.Count > 0) return results;
        }

        // ── Semantic search (new) ──
        if (_semanticSearch is not null)
        {
            var query = !string.IsNullOrWhiteSpace(request.Task)
                ? request.Task
                : request.Query;

            var searchResults = _semanticSearch.Search(query, topK: 15);
            var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sr in searchResults)
            {
                if (!seenNodeIds.Add(sr.ChunkId)) continue;

                var node = _graphQuery.GetNode(sr.ChunkId);
                if (node is null || node.IsExternal || string.IsNullOrEmpty(node.SourceFile))
                    continue;

                var symbol = BuildSymbol(node);
                if (symbol is not null)
                {
                    symbol.Callees.Clear(); // will be enriched below
                    symbol.Callers.Clear();
                    results.Add(symbol);
                }

                if (results.Count >= 10) break;
            }

            if (results.Count > 0)
            {
                foreach (var symbol in results)
                    EnrichRelations(symbol);
                return results;
            }
        }

        // ── Keyword fallback ──
        var keywords = request.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (keywords.Length == 0 && !string.IsNullOrWhiteSpace(request.TargetMethodName))
            keywords = new[] { request.TargetMethodName };

        var allNodes = _graphQuery.GetAllNodes()
            .Where(n => !n.IsExternal && !string.IsNullOrEmpty(n.SourceFile))
            .ToList();

        var scored = new List<(GraphNode Node, int Score)>();
        foreach (var node in allNodes)
        {
            var score = 0;
            foreach (var kw in keywords)
            {
                if (string.Equals(node.MethodName, kw, StringComparison.OrdinalIgnoreCase))
                    score += 5;
                else if (node.MethodName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    score += 3;
                if (node.ClassName is not null && node.ClassName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    score += 2;
                if (node.Label.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    score += 1;
            }
            if (score > 0) scored.Add((node, score));
        }

        foreach (var (node, _) in scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Node.Label, StringComparer.Ordinal)
            .Take(10))
        {
            var symbol = BuildSymbol(node);
            if (symbol is not null && !results.Any(r => r.NodeId == symbol.NodeId))
                results.Add(symbol);
        }

        // Enrich with callee/caller info
        foreach (var symbol in results)
        {
            EnrichRelations(symbol);
        }

        return results;
    }

    private LocatedSymbol? FindMethod(string filePath, string methodName)
    {
        var node = _graphQuery.GetAllNodes()
            .FirstOrDefault(n =>
                n.SourceFile.Contains(filePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(n.MethodName, methodName, StringComparison.Ordinal));

        return node is not null ? BuildSymbol(node) : null;
    }

    public LocatedSymbol? BuildSymbol(GraphNode node)
    {
        if (string.IsNullOrEmpty(node.SourceFile) || !File.Exists(node.SourceFile))
            return null;

        try
        {
            var lines = File.ReadAllLines(node.SourceFile);
            var (start, end) = FindMethodRange(lines, node.MethodName);
            var body = ExtractBody(lines, start, end);
            var isPrivate = DetectPrivate(lines, start, end);
            var isPublicApi = DetectPublicApi(node);

            return new LocatedSymbol
            {
                NodeId = node.Id,
                MethodName = node.MethodName,
                ClassName = node.ClassName ?? "",
                Namespace = node.Namespace,
                SourceFilePath = node.SourceFile,
                MethodStartLine = start,
                MethodEndLine = end,
                MethodBody = body,
                FullSignature = node.Label,
                IsPrivate = isPrivate,
                IsPublicApi = isPublicApi,
                ParameterTypes = node.ParameterTypes,
            };
        }
        catch
        {
            return null;
        }
    }

    private void EnrichRelations(LocatedSymbol symbol)
    {
        var calleeIds = _graphQuery.GetCallees(symbol.NodeId);
        var callerIds = _graphQuery.GetCallers(symbol.NodeId);

        foreach (var calleeId in calleeIds.Take(5))
        {
            var node = _graphQuery.GetNode(calleeId);
            if (node is not null)
            {
                var s = BuildSymbol(node);
                if (s is not null) symbol.Callees.Add(s);
            }
        }

        foreach (var callerId in callerIds.Take(3))
        {
            var node = _graphQuery.GetNode(callerId);
            if (node is not null)
            {
                var s = BuildSymbol(node);
                if (s is not null) symbol.Callers.Add(s);
            }
        }
    }

    private static (int Start, int End) FindMethodRange(string[] lines, string methodName)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Contains(methodName, StringComparison.Ordinal)
                && (trimmed.Contains("(", StringComparison.Ordinal)))
            {
                var braceCount = 0;
                var foundOpen = false;
                var end = i;
                for (var j = i; j < lines.Length; j++)
                {
                    foreach (var c in lines[j])
                    {
                        if (c == '{') { braceCount++; foundOpen = true; }
                        if (c == '}') braceCount--;
                    }
                    if (foundOpen && braceCount == 0) { end = j; break; }
                    if (j == lines.Length - 1) end = j;
                }
                return (i + 1, end + 1);
            }
        }
        return (1, lines.Length);
    }

    private static string ExtractBody(string[] lines, int start, int end)
    {
        if (start < 1 || end > lines.Length || end < start) return "";
        var bodyLines = lines.Skip(start - 1).Take(end - start + 1);
        return string.Join("\n", bodyLines);
    }

    private static bool DetectPrivate(string[] lines, int start, int end)
    {
        for (var i = Math.Max(0, start - 3); i < Math.Min(start, lines.Length); i++)
        {
            if (lines[i].TrimStart().StartsWith("private ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool DetectPublicApi(GraphNode node)
    {
        return node.Attributes.ContainsKey("aspnet-route:entry-point")
            || (node.ClassName?.EndsWith("Controller", StringComparison.Ordinal) ?? false);
    }
}

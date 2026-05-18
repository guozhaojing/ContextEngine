// =============================================================================
// Graph/Analysis/GraphAnalysisMergeService.cs — 将分析结果合并进图（唯一写入入口）
// =============================================================================
// 合并后重新物化 CalledBy 并重建 GraphIndex。
// 增量模式：先按 analyzer + sourceFile 删除旧贡献，再写入新结果。
// =============================================================================

using Core.Graph.Indexing;

namespace Core.Graph.Analysis;

/// <summary>
/// 把 Facts / Annotations / ExtraEdges 合并到 CodeGraph 的副本中。
/// </summary>
public sealed class GraphAnalysisMergeService
{
    public CodeGraphBuildResult Merge(
        CodeGraph baseGraph,
        GraphAnalysisRunResult analysisRun,
        GraphAnalysisScope scope)
    {
        ArgumentNullException.ThrowIfNull(baseGraph);
        ArgumentNullException.ThrowIfNull(analysisRun);

        var graph = CloneGraph(baseGraph);

        if (!scope.IsFullScan)
            RemoveAnalyzerContributions(graph, analysisRun, scope);

        ApplyFacts(graph, analysisRun.AllFacts);
        ApplyAnnotations(graph, analysisRun.AllAnnotations);
        ApplyExtraEdges(graph, analysisRun.AllExtraEdges);

        GraphAdjacencyMaterializer.Apply(graph);
        var index = GraphIndex.Build(graph);

        return new CodeGraphBuildResult
        {
            Graph = graph,
            Index = index
        };
    }

    private static void RemoveAnalyzerContributions(
        CodeGraph graph,
        GraphAnalysisRunResult analysisRun,
        GraphAnalysisScope scope)
    {
        var analyzerNames = analysisRun.AnalyzerResults
            .Select(result => result.AnalyzerName)
            .ToHashSet(StringComparer.Ordinal);

        graph.Facts.RemoveAll(fact =>
            analyzerNames.Contains(fact.Analyzer)
            && IsInScope(fact.SourceFile, scope));

        graph.Edges.RemoveAll(edge =>
            edge.Attributes.TryGetValue("analyzer", out var analyzer)
            && analyzerNames.Contains(analyzer)
            && IsInScope(
                edge.Attributes.TryGetValue("sourceFile", out var sourceFile) ? sourceFile : null,
                scope));

        foreach (var node in graph.Nodes)
        {
            var keysToRemove = node.Attributes.Keys
                .Where(key => TryGetAnalyzerFromAttributeKey(key, out var analyzer)
                    && analyzerNames.Contains(analyzer)
                    && IsInScope(node.Attributes.TryGetValue("_sourceFile", out var sf) ? sf : null, scope))
                .ToList();

            foreach (var key in keysToRemove)
                node.Attributes.Remove(key);
        }
    }

    private static bool TryGetAnalyzerFromAttributeKey(string attributeKey, out string analyzer)
    {
        analyzer = "";
        var separator = attributeKey.IndexOf(':');
        if (separator <= 0)
            return false;

        analyzer = attributeKey[..separator];
        return true;
    }

    private static bool IsInScope(string? sourceFile, GraphAnalysisScope scope)
    {
        if (scope.IsFullScan)
            return true;

        if (string.IsNullOrWhiteSpace(sourceFile))
            return false;

        return scope.ShouldAnalyzeFile(sourceFile);
    }

    private static void ApplyFacts(CodeGraph graph, IEnumerable<GraphFact> facts)
    {
        foreach (var fact in facts)
            graph.Facts.Add(CloneFact(fact));
    }

    private static void ApplyAnnotations(CodeGraph graph, IEnumerable<GraphAnnotation> annotations)
    {
        var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        foreach (var annotation in annotations)
        {
            if (!nodesById.TryGetValue(annotation.TargetMethodId, out var node))
                continue;

            var attributeKey = BuildAnnotationKey(annotation);
            node.Attributes[attributeKey] = annotation.Value;

            if (!string.IsNullOrWhiteSpace(annotation.SourceFile))
            {
                node.Attributes["_sourceFile"] =
                    GraphAnalysisScope.NormalizeFilePath(annotation.SourceFile);
            }
        }
    }

    private static void ApplyExtraEdges(CodeGraph graph, IEnumerable<GraphExtraEdge> extraEdges)
    {
        var nodeIds = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var extra in extraEdges)
        {
            var attributes = new Dictionary<string, string>(extra.Attributes, StringComparer.Ordinal);
            attributes["analyzer"] = attributes.TryGetValue("analyzer", out var name) && !string.IsNullOrEmpty(name)
                ? name
                : "analysis";

            if (!string.IsNullOrWhiteSpace(extra.SourceFile))
                attributes["sourceFile"] = GraphAnalysisScope.NormalizeFilePath(extra.SourceFile);

            graph.Edges.Add(new GraphEdge
            {
                FromId = extra.FromId,
                ToId = extra.ToId,
                Call = extra.Label,
                Kind = extra.Kind,
                IsResolved = extra.IsResolved,
                Attributes = attributes
            });

            if (!nodeIds.Contains(extra.ToId))
            {
                graph.Nodes.Add(new GraphNode
                {
                    Id = extra.ToId,
                    Kind = GraphNodeKind.External,
                    Label = extra.Label,
                    ClassName = "(external)",
                    MethodName = extra.Label,
                    IsExternal = true
                });
                nodeIds.Add(extra.ToId);
            }
        }
    }

    private static string BuildAnnotationKey(GraphAnnotation annotation)
    {
        if (annotation.Key.Contains(':', StringComparison.Ordinal))
            return annotation.Key;

        return $"{annotation.Analyzer}:{annotation.Key}";
    }

    private static GraphFact CloneFact(GraphFact fact) => new()
    {
        Analyzer = fact.Analyzer,
        SubjectId = fact.SubjectId,
        SubjectKind = fact.SubjectKind,
        FactType = fact.FactType,
        SourceFile = fact.SourceFile,
        Data = new Dictionary<string, string>(fact.Data, StringComparer.Ordinal)
    };

    private static CodeGraph CloneGraph(CodeGraph source) => new()
    {
        ScanRoot = source.ScanRoot,
        SchemaVersion = source.SchemaVersion,
        Nodes = source.Nodes.Select(CloneNode).ToList(),
        Edges = source.Edges.Select(CloneEdge).ToList(),
        Facts = source.Facts.Select(CloneFact).ToList()
    };

    private static GraphNode CloneNode(GraphNode node) => new()
    {
        Id = node.Id,
        Kind = node.Kind,
        Label = node.Label,
        ProjectName = node.ProjectName,
        ProjectPath = node.ProjectPath,
        Namespace = node.Namespace,
        ClassName = node.ClassName,
        MethodName = node.MethodName,
        ParameterTypes = node.ParameterTypes.ToList(),
        IsExternal = node.IsExternal,
        CalledBy = node.CalledBy.ToList(),
        Attributes = new Dictionary<string, string>(node.Attributes, StringComparer.Ordinal)
    };

    private static GraphEdge CloneEdge(GraphEdge edge) => new()
    {
        FromId = edge.FromId,
        ToId = edge.ToId,
        Call = edge.Call,
        Kind = edge.Kind,
        IsResolved = edge.IsResolved,
        Attributes = new Dictionary<string, string>(edge.Attributes, StringComparer.Ordinal)
    };
}

using Core.Models;
using Core.Scanning;

namespace Core.Graph.Analysis;

/// <summary>
/// 分析器只读上下文：提供扫描结果与基础图快照，分析产出写入 <see cref="Result"/>。
/// </summary>
public sealed class GraphAnalysisContext
{
    private GraphAnalysisContext(
        SolutionScanResult scan,
        IGraphSnapshot baseGraph,
        GraphAnalysisScope scope,
        GraphAnalysisResult result)
    {
        Scan = scan;
        BaseGraph = baseGraph;
        Scope = scope;
        Result = result;
        UnitsByFile = BuildUnitsByFile(scan);
        NodesById = baseGraph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
    }

    public SolutionScanResult Scan { get; }

    public IGraphSnapshot BaseGraph { get; }

    public GraphAnalysisScope Scope { get; }

    public GraphAnalysisResult Result { get; }

    public IReadOnlyDictionary<string, GraphNode> NodesById { get; }

    public ILookup<string, CodeUnit> UnitsByFile { get; }

    public static GraphAnalysisContext Create(
        SolutionScanResult scan,
        CodeGraph baseGraph,
        GraphAnalysisScope scope,
        IGraphAnalyzer analyzer) =>
        new(scan, new GraphSnapshotView(baseGraph), scope, new GraphAnalysisResult
        {
            AnalyzerName = analyzer.Name
        });

    public IEnumerable<CodeUnit> GetUnitsInScope()
    {
        foreach (var unit in Scan.AllCodeUnits)
        {
            if (Scope.ShouldAnalyzeFile(unit.RelativeFilePath))
                yield return unit;
        }
    }

    public void AddFact(
        string subjectId,
        string factType,
        string subjectKind = GraphSubjectKinds.Method,
        string? sourceFile = null,
        IDictionary<string, string>? data = null)
    {
        Result.Facts.Add(new GraphFact
        {
            Analyzer = Result.AnalyzerName,
            SubjectId = subjectId,
            SubjectKind = subjectKind,
            FactType = factType,
            SourceFile = sourceFile,
            Data = data is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(data, StringComparer.Ordinal)
        });
    }

    public void AddAnnotation(
        string targetMethodId,
        string key,
        string value,
        string? sourceFile = null)
    {
        Result.Annotations.Add(new GraphAnnotation
        {
            Analyzer = Result.AnalyzerName,
            TargetMethodId = targetMethodId,
            Key = key,
            Value = value,
            SourceFile = sourceFile
        });
    }

    public void AddExtraEdge(
        string fromId,
        string toId,
        string kind,
        string label,
        bool isResolved,
        string? sourceFile = null,
        IDictionary<string, string>? attributes = null)
    {
        var edge = new GraphExtraEdge
        {
            FromId = fromId,
            ToId = toId,
            Kind = kind,
            Label = label,
            IsResolved = isResolved,
            SourceFile = sourceFile
        };

        if (attributes is not null)
        {
            foreach (var pair in attributes)
                edge.Attributes[pair.Key] = pair.Value;
        }

        edge.Attributes["analyzer"] = Result.AnalyzerName;
        Result.ExtraEdges.Add(edge);
    }

    private static ILookup<string, CodeUnit> BuildUnitsByFile(SolutionScanResult scan) =>
        scan.AllCodeUnits.ToLookup(
            unit => GraphAnalysisScope.NormalizeFilePath(unit.RelativeFilePath),
            StringComparer.OrdinalIgnoreCase);

    private sealed class GraphSnapshotView : IGraphSnapshot
    {
        public GraphSnapshotView(CodeGraph graph)
        {
            ScanRoot = graph.ScanRoot;
            SchemaVersion = graph.SchemaVersion;
            Nodes = graph.Nodes;
            Edges = graph.Edges;
            Facts = graph.Facts;
        }

        public string ScanRoot { get; }

        public int SchemaVersion { get; }

        public IReadOnlyList<GraphNode> Nodes { get; }

        public IReadOnlyList<GraphEdge> Edges { get; }

        public IReadOnlyList<GraphFact> Facts { get; }
    }
}

public interface IGraphSnapshot
{
    string ScanRoot { get; }

    int SchemaVersion { get; }

    IReadOnlyList<GraphNode> Nodes { get; }

    IReadOnlyList<GraphEdge> Edges { get; }

    IReadOnlyList<GraphFact> Facts { get; }
}

// =============================================================================
// Graph/CodeGraphBuilder.cs — 基础调用图构建器
// =============================================================================
// 职责：CodeUnit → Nodes + Edges（仅 call 类型）
// 不做：查询、分析器、直接改 GraphNode（除创建节点时的基本字段）
// 流程：注册表 → 节点 → 语义边 → 外部节点补全 → CalledBy 物化 → GraphIndex
// =============================================================================

using Core.Graph.Building;
using Core.Graph.Identity;
using Core.Graph.Indexing;
using Core.Models;
using Core.Scanning;
using Core.Semantics;

namespace Core.Graph;

public static class CodeGraphBuilder
{
    /// <summary>
    /// 从扫描结果构建基础调用图及查询索引。
    /// </summary>
    public static CodeGraphBuildResult Build(SolutionScanResult scan)
    {
        var units = scan.AllCodeUnits;
        var graph = new CodeGraph { ScanRoot = scan.ScanRoot };
        var registry = new MethodRegistry(units);
        var targetResolver = new SemanticCallTargetResolver(registry);
        var nodeMap = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

        // 第一步：每个 CodeUnit 一个节点
        foreach (var unit in units)
            AddNode(graph, nodeMap, unit);

        // 第二步：根据 ResolvedCalls 连边
        foreach (var unit in units)
            AddEdgesForUnit(graph, nodeMap, targetResolver, unit);

        // 第三步：为指向外部的边创建 external 节点
        EnsureExternalNodes(graph, nodeMap);

        // 第四步：从边生成 CalledBy，并构建邻接索引
        GraphAdjacencyMaterializer.Apply(graph);
        var index = GraphIndex.Build(graph);

        return new CodeGraphBuildResult
        {
            Graph = graph,
            Index = index
        };
    }

    [Obsolete("Use MethodIdBuilder.FromCodeUnit instead.")]
    public static string ToNodeId(CodeUnit unit) => MethodIdBuilder.FromCodeUnit(unit).Value;

    private static void AddNode(
        CodeGraph graph,
        Dictionary<string, GraphNode> nodeMap,
        CodeUnit unit)
    {
        var id = MethodIdBuilder.FromCodeUnit(unit).Value;

        if (nodeMap.ContainsKey(id))
            return;

        var label = unit.ParameterTypes.Count > 0
            ? $"{unit.ClassName}.{unit.MethodName}({string.Join(", ", unit.ParameterTypes)})"
            : $"{unit.ClassName}.{unit.MethodName}";

        var node = new GraphNode
        {
            Id = id,
            Kind = GraphNodeKind.Method,
            Label = label,
            ProjectName = unit.ProjectName,
            ProjectPath = unit.ProjectPath,
            Namespace = unit.Namespace,
            ClassName = unit.ClassName,
            MethodName = unit.MethodName,
            ParameterTypes = unit.ParameterTypes.ToList(),
            IsExternal = false
        };

        graph.Nodes.Add(node);
        nodeMap[id] = node;
    }

    private static void AddEdgesForUnit(
        CodeGraph graph,
        Dictionary<string, GraphNode> nodeMap,
        SemanticCallTargetResolver targetResolver,
        CodeUnit unit)
    {
        var fromId = MethodIdBuilder.FromCodeUnit(unit).Value;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resolved in unit.ResolvedCalls)
        {
            if (!targetResolver.TryResolveTargetId(resolved, unit.ProjectPath, out var targetId))
                continue;

            if (!seenTargets.Add($"{fromId}->{targetId}"))
                continue;

            var isResolved = nodeMap.ContainsKey(targetId) && !nodeMap[targetId].IsExternal;
            graph.Edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = targetId,
                Call = ResolvedMethodInfoFormatter.ToQualifiedName(resolved),
                IsResolved = isResolved,
                Kind = GraphEdgeKinds.Call
            });
        }
    }

    private static void EnsureExternalNodes(CodeGraph graph, Dictionary<string, GraphNode> nodeMap)
    {
        foreach (var edge in graph.Edges)
        {
            if (nodeMap.ContainsKey(edge.ToId))
                continue;

            var externalNode = new GraphNode
            {
                Id = edge.ToId,
                Kind = GraphNodeKind.External,
                Label = edge.Call,
                ClassName = "(external)",
                MethodName = edge.Call,
                IsExternal = true
            };

            graph.Nodes.Add(externalNode);
            nodeMap[edge.ToId] = externalNode;
        }
    }
}

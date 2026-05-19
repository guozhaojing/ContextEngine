// =============================================================================
// Context/ContextTraceMap.cs — full trace from context section back to source
// =============================================================================
// Maps each context section through the pipeline:
//   Controller → Method → DAO → Entity → NH access → Traversal path → Context section
// Every step traceable to source file and symbol with confidence/evidence.
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Retrieval.Chunking;
using Core.Semantics;
using Core.Truth;

namespace Core.Context;

public sealed class ContextTraceMap
{
    private readonly GraphQueryService _query;
    private readonly SymbolReferenceIndex? _symbolIndex;

    public ContextTraceMap(GraphQueryService query, SymbolReferenceIndex? symbolIndex = null)
    {
        _query = query;
        _symbolIndex = symbolIndex;
    }

    public ContextTrace ResolveFullTrace(
        string sectionTitle,
        ContextSectionKind kind,
        IReadOnlyList<string> sourceNodeIds)
    {
        var steps = new List<TraceStep>();

        foreach (var nodeId in sourceNodeIds.Take(20))
        {
            var node = _query.GetNode(nodeId);
            if (node is null) continue;

            steps.Add(TraceStep.FromGraphNode(node, _query));

            switch (node.Kind)
            {
                case GraphNodeKind.Method:
                    AddCallTraceSteps(steps, node);
                    AddEntityAccessSteps(steps, node);
                    break;
                case GraphNodeKind.Entity:
                    AddEntityDetails(steps, node);
                    break;
            }
        }

        return new ContextTrace
        {
            SectionTitle = sectionTitle,
            SectionKind = kind,
            Steps = steps,
            TotalSteps = steps.Count,
            IsFullyTraceable = steps.All(s => s.HasSourceFile),
            HasSymbolBinding = steps.Any(s => !s.SymbolHandle.IsEmpty),
        };
    }

    public string GenerateTraceReport(ContextTrace trace)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Trace Report: {trace.SectionTitle}");
        sb.AppendLine($"Section Kind: {trace.SectionKind}");
        sb.AppendLine($"Fully Traceable: {trace.IsFullyTraceable}");
        sb.AppendLine($"Symbol Bindings: {trace.HasSymbolBinding}");
        sb.AppendLine();

        for (var i = 0; i < trace.Steps.Count; i++)
        {
            var step = trace.Steps[i];
            sb.AppendLine($"### Step {i + 1}: {step.NodeKind} → {step.NodeLabel}");
            sb.AppendLine($"  Source File: {step.SourceFile}");
            sb.AppendLine($"  Symbol: {step.SymbolHandle}");
            sb.AppendLine($"  Confidence: {step.Confidence}");
            sb.AppendLine($"  Evidence: {step.Evidence}");
            sb.AppendLine($"  Grounded: {step.IsGrounded}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void AddCallTraceSteps(List<TraceStep> steps, GraphNode methodNode)
    {
        var outgoing = _query.GetOutgoingEdges(methodNode.Id);
        foreach (var edge in outgoing.Take(10))
        {
            var target = _query.GetNode(edge.ToId);
            if (target is null) continue;

            steps.Add(TraceStep.FromEdge(edge, target));
        }
    }

    private void AddEntityAccessSteps(List<TraceStep> steps, GraphNode methodNode)
    {
        var entityEdges = _query.GetOutgoingEdges(methodNode.Id)
            .Where(e => e.Kind is "nh:entity-access" or "nh:query" or "nh:save")
            .ToList();

        foreach (var edge in entityEdges.Take(5))
        {
            var entityNode = _query.GetNode(edge.ToId);
            if (entityNode is null) continue;

            var step = TraceStep.FromGraphNode(entityNode, _query);
            step.EdgeKind = edge.Kind;
            step.ParentStepLabel = methodNode.Label;
            steps.Add(step);
        }
    }

    private void AddEntityDetails(List<TraceStep> steps, GraphNode entityNode)
    {
        var incoming = _query.GetIncomingEdges(entityNode.Id);
        foreach (var edge in incoming.Take(5))
        {
            var caller = _query.GetNode(edge.ToId);  // Incoming: ToId = the source node
            if (caller is null) continue;

            var step = TraceStep.FromGraphNode(caller, _query);
            step.EdgeKind = edge.Kind;
            step.ParentStepLabel = entityNode.Label;
            steps.Add(step);
        }
    }
}

public sealed class ContextTrace
{
    public required string SectionTitle { get; init; }
    public ContextSectionKind SectionKind { get; init; }
    public required IReadOnlyList<TraceStep> Steps { get; init; }
    public int TotalSteps { get; init; }
    public bool IsFullyTraceable { get; init; }
    public bool HasSymbolBinding { get; init; }
}

public sealed class TraceStep
{
    public string NodeId { get; set; } = "";
    public string NodeLabel { get; set; } = "";
    public string NodeKind { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public SymbolHandle SymbolHandle { get; set; }
    public string Confidence { get; set; } = "";
    public string Evidence { get; set; } = "";
    public bool IsGrounded { get; set; }
    public string? EdgeKind { get; set; }
    public string? ParentStepLabel { get; set; }

    public bool HasSourceFile => !string.IsNullOrEmpty(SourceFile);

    public static TraceStep FromGraphNode(GraphNode node, GraphQueryService query)
    {
        var handleStr = node.Attributes.GetValueOrDefault("symbolHandle", "");
        SymbolHandle.TryParse(handleStr, out var handle);

        return new TraceStep
        {
            NodeId = node.Id,
            NodeLabel = node.Label,
            NodeKind = node.Kind,
            SourceFile = node.Attributes.GetValueOrDefault("sourceFile", ""),
            SymbolHandle = handle,
            Confidence = node.Attributes.GetValueOrDefault("confidence", "unknown"),
            Evidence = node.Attributes.GetValueOrDefault("evidence", "none"),
            IsGrounded = !string.IsNullOrEmpty(handleStr),
        };
    }

    public static TraceStep FromEdge(EdgeInfo edge, GraphNode targetNode)
    {
        var step = FromGraphNode(targetNode, null!);
        step.EdgeKind = edge.Kind;
        return step;
    }
}

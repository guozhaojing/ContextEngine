// =============================================================================
// Cognition/BusinessCapabilityMapper.cs — maps capabilities to code regions
// =============================================================================
// Determinism: capability detection uses fixed keyword matching + graph structure.
// Provenance: every capability mapping cites the specific nodes and files.
// Replay: BusinessCapabilityResult is structurally comparable.
// Grounding: capabilities are traced through execution chains from API to data.
// =============================================================================

using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.Grounding.Confidence;
using Core.Semantics;

namespace Core.Cognition;

public sealed class BusinessCapabilityMapper
{
    private readonly GraphQueryService _graphQuery;
    private readonly SymbolReferenceIndex _symbolIndex;
    private readonly BusinessCapabilityOptions _options;

    public BusinessCapabilityMapper(
        GraphQueryService graphQuery,
        SymbolReferenceIndex symbolIndex,
        BusinessCapabilityOptions? options = null)
    {
        _graphQuery = graphQuery ?? throw new ArgumentNullException(nameof(graphQuery));
        _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
        _options = options ?? BusinessCapabilityOptions.Default;
    }

    public CognitionResult Map(string query)
    {
        var resultId = $"bizcap-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var explanations = new List<GroundedExplanation>();
        var citations = new List<EvidenceReference>();
        var expId = 0;
        var citId = 0;

        var capabilities = DiscoverCapabilities();
        var matched = MatchCapabilities(query, capabilities);

        if (matched.Count == 0)
        {
            matched = capabilities
                .OrderByDescending(c => c.Confidence)
                .Take(5)
                .ToList();
        }

        var overallConfidence = matched.Count > 0
            ? matched.Min(c => c.Confidence)
            : ConfidenceLevel.Weak;

        explanations.Add(DescribeCapabilityOverview(matched.Count, capabilities.Count, ref expId));
        explanations.AddRange(DescribeMatchedCapabilities(matched, ref expId, ref citId, citations));
        explanations.AddRange(DescribeExecutionChains(matched, ref expId, ref citId, citations));
        explanations.Add(DescribeHiddenDependencies(matched, ref expId, ref citId, citations));

        return new CognitionResult
        {
            ResultId = resultId,
            Query = query,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ResultType = CognitionResultType.BusinessCapabilityMap,
            Explanations = explanations,
            Citations = citations,
            OverallConfidence = overallConfidence,
        };
    }

    private List<BusinessCapability> DiscoverCapabilities()
    {
        var capabilities = new List<BusinessCapability>();
        var allNodes = _graphQuery.GetAllNodes().ToList();

        var serviceNodes = allNodes
            .Where(n => !n.IsExternal && !string.IsNullOrEmpty(n.ClassName))
            .Where(n => n.ClassName is not null && (
                n.ClassName.EndsWith("Service", StringComparison.Ordinal)
                || n.ClassName.EndsWith("Manager", StringComparison.Ordinal)
                || n.ClassName.EndsWith("Orchestrator", StringComparison.Ordinal)
                || n.ClassName.EndsWith("Handler", StringComparison.Ordinal)
                || n.ClassName.EndsWith("Processor", StringComparison.Ordinal)
                || n.ClassName.EndsWith("Job", StringComparison.Ordinal)))
            .OrderBy(n => n.Label, StringComparer.Ordinal)
            .ToList();

        foreach (var node in serviceNodes.Take(50))
        {
            var callees = _graphQuery.GetCallees(node.Id)
                .Select(id => _graphQuery.GetNode(id))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();

            var calleeLabels = callees
                .OrderBy(c => c.Label, StringComparer.Ordinal)
                .Select(c => c.Label)
                .Take(5)
                .ToList();

            var entityNodes = callees
                .Where(c => c.Kind == "entity" || c.GroundingKind == "nh:entity")
                .ToList();

            var entryPoints = _graphQuery.FindEntryPoints(node.Id);

            var confidence = entityNodes.Count > 0
                ? ConfidenceLevel.Strong
                : callees.Count > 3
                    ? ConfidenceLevel.Moderate
                    : ConfidenceLevel.Weak;

            capabilities.Add(new BusinessCapability
            {
                CapabilityName = ExtractBusinessName(node),
                ServiceNodeId = node.Id,
                ServiceLabel = node.Label,
                SourceFile = node.SourceFile,
                CalledServices = calleeLabels,
                EntityCount = entityNodes.Count,
                EntryPointCount = entryPoints.Count,
                HasApiEntry = entryPoints.Count > 0,
                HasDataAccess = entityNodes.Count > 0,
                HasOrchestration = callees.Count > 5,
                Confidence = confidence,
            });
        }

        return capabilities;
    }

    private List<BusinessCapability> MatchCapabilities(string query, List<BusinessCapability> capabilities)
    {
        var keywords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(BusinessCapability Cap, int Score)>();

        foreach (var cap in capabilities)
        {
            var score = 0;
            var searchIn = $"{cap.CapabilityName} {cap.ServiceLabel} {string.Join(" ", cap.CalledServices)}".ToLowerInvariant();

            foreach (var kw in keywords)
            {
                if (searchIn.Contains(kw, StringComparison.Ordinal))
                    score += 3;
            }

            if (score > 0)
                scored.Add((cap, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Cap.CapabilityName, StringComparer.Ordinal)
            .Take(10)
            .Select(s => s.Cap)
            .ToList();
    }

    private static string ExtractBusinessName(GraphNode node)
    {
        var className = node.ClassName ?? "";
        var suffixes = new[] { "Service", "Manager", "Orchestrator", "Handler", "Processor", "Job" };
        var name = className;
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return string.IsNullOrEmpty(name) ? className : name;
    }

    private GroundedExplanation DescribeCapabilityOverview(int matched, int total, ref int expId)
    {
        return new GroundedExplanation
        {
            ExplanationId = $"bizcap-exp-{expId++:D5}",
            Text = $"Identified {matched} matching business capabilities out of {total} discovered service nodes.",
            Claim = "Capability overview",
            ConfidenceLevel = matched > 0 ? ConfidenceLevel.Strong : ConfidenceLevel.Moderate,
            SupportingNodeIds = Array.Empty<string>(),
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        };
    }

    private IReadOnlyList<GroundedExplanation> DescribeMatchedCapabilities(
        List<BusinessCapability> capabilities,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var cap in capabilities.OrderByDescending(c => c.Confidence))
        {
            var text = $"{cap.CapabilityName}: {cap.ServiceLabel}";
            var details = new List<string>();

            if (cap.HasApiEntry) details.Add("has API entry point(s)");
            if (cap.HasDataAccess) details.Add("accesses entity/data");
            if (cap.HasOrchestration) details.Add("orchestrates multiple services");
            if (cap.CalledServices.Count > 0)
                details.Add($"calls: {string.Join(", ", cap.CalledServices.Take(3))}");

            text += $" ({string.Join("; ", details)})";
            var citIds = new List<string>();

            var node = _graphQuery.GetNode(cap.ServiceNodeId);
            if (node is not null)
            {
                citations.Add(new EvidenceReference
                {
                    CitationId = $"cite-{citId++:D5}",
                    SourceNodeId = cap.ServiceNodeId,
                    SourceNodeLabel = cap.ServiceLabel,
                    SourceFile = cap.SourceFile,
                    SymbolHandle = node.SymbolHandle,
                    ConfidenceLevel = cap.Confidence,
                });
                citIds.Add($"cite-{citations.Last().CitationId}");
            }

            explanations.Add(new GroundedExplanation
            {
                ExplanationId = $"bizcap-exp-{expId++:D5}",
                Text = text,
                Claim = $"Capability: {cap.CapabilityName}",
                ConfidenceLevel = cap.Confidence,
                SupportingNodeIds = new[] { cap.ServiceNodeId },
                SupportingSourceFiles = new[] { cap.SourceFile }.Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CitationIds = citIds,
            });
        }

        return explanations;
    }

    private IReadOnlyList<GroundedExplanation> DescribeExecutionChains(
        List<BusinessCapability> capabilities,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var explanations = new List<GroundedExplanation>();

        foreach (var cap in capabilities.Take(5))
        {
            var chains = _graphQuery.GetCallChain(cap.ServiceNodeId, 3);
            if (chains.Count > 0)
            {
                var pathIds = chains.SelectMany(c => c).Distinct(StringComparer.Ordinal).Take(5).ToList();
                var labels = pathIds
                    .Select(id => _graphQuery.GetNode(id)?.Label ?? ShortenId(id))
                    .ToList();

                explanations.Add(new GroundedExplanation
                {
                    ExplanationId = $"bizcap-exp-{expId++:D5}",
                    Text = $"Execution chain for {cap.CapabilityName}: → {string.Join(" → ", labels)}",
                    Claim = $"Execution chain: {cap.CapabilityName}",
                    ConfidenceLevel = cap.Confidence,
                    SupportingNodeIds = pathIds,
                    SupportingSourceFiles = Array.Empty<string>(),
                    CitationIds = Array.Empty<string>(),
                });
            }
        }

        return explanations;
    }

    private GroundedExplanation DescribeHiddenDependencies(
        List<BusinessCapability> capabilities,
        ref int expId,
        ref int citId,
        List<EvidenceReference> citations)
    {
        var hiddenCount = capabilities.Count(c => c.EntityCount == 0 && c.EntryPointCount == 0);

        var text = hiddenCount > 0
            ? $"Note: {hiddenCount} capability/capabilities have neither direct API entry points nor entity access — these may be internal orchestrators or utility services."
            : "All matched capabilities have either API entry points or data access paths.";

        return new GroundedExplanation
        {
            ExplanationId = $"bizcap-exp-{expId++:D5}",
            Text = text,
            Claim = "Hidden dependencies",
            ConfidenceLevel = hiddenCount > 0 ? ConfidenceLevel.Moderate : ConfidenceLevel.Strong,
            SupportingNodeIds = Array.Empty<string>(),
            SupportingSourceFiles = Array.Empty<string>(),
            CitationIds = Array.Empty<string>(),
        };
    }

    private static string ShortenId(string id)
    {
        return id.Contains("::") ? id[(id.LastIndexOf("::") + 2)..] : id;
    }
}

public class BusinessCapabilityOptions
{
    public int MaxCapabilities { get; init; } = 50;
    public int MaxResults { get; init; } = 10;

    public static BusinessCapabilityOptions Default => new();
}

public sealed class BusinessCapability
{
    public required string CapabilityName { get; init; }
    public required string ServiceNodeId { get; init; }
    public required string ServiceLabel { get; init; }
    public string SourceFile { get; init; } = "";
    public required IReadOnlyList<string> CalledServices { get; init; }
    public int EntityCount { get; init; }
    public int EntryPointCount { get; init; }
    public bool HasApiEntry { get; init; }
    public bool HasDataAccess { get; init; }
    public bool HasOrchestration { get; init; }
    public ConfidenceLevel Confidence { get; init; }
}

using System.Text.Json;
using Core.Cognition;
using Core.Cognition.Epistemics;
using Core.Cognition.Patching;
using Core.Experience;
using Core.Graph;
using Core.Graph.Indexing;
using Core.Graph.Query;
using Core.SelfValidation;
using Core.Verification;

namespace App.Mcp;

public sealed class ContextEngineMcpTools
{
    private readonly GraphQueryService _query;
    private readonly RepositorySession? _session;
    private readonly McpToolDefinition[] _definitions;

    public ContextEngineMcpTools(GraphQueryService query, RepositorySession? session = null)
    {
        _query = query;
        _session = session;
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
            "ce_ask" => Ask(args),
            "ce_verify" => Verify(args),
            "ce_self_critique" => SelfCritique(args),
            "ce_epistemic_boundary" => EpistemicBoundary(args),
            "ce_patch" => Patch(args),
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

    // ── Cognitive tools ──

    private object Ask(Dictionary<string, JsonElement> args)
    {
        EnsureSession();
        var question = GetStringArg(args, "question");
        var result = _session!.Query(question);
        return SerializeCognitionResult(result);
    }

    private object Verify(Dictionary<string, JsonElement> args)
    {
        EnsureSession();
        var question = GetStringArg(args, "question");
        var result = _session!.Query(question);

        var epistemic = new EpistemicBoundary(_session.QueryService!).Analyze(result, question);
        var orchestrator = new VerificationOrchestrator();
        var report = orchestrator.Verify(result, epistemic);

        return new
        {
            question,
            verdict = report.Verdict.ToString(),
            verdictDisplay = report.Verdict.ToDisplayText(),
            trustScore = Math.Round(report.OverallTrustScore, 3),
            summary = report.Summary,
            grounding = new
            {
                score = report.Grounding.Score,
                totalCitations = report.Grounding.TotalCitations,
                citationsWithFiles = report.Grounding.CitationsWithFiles,
                citationsWithSymbols = report.Grounding.CitationsWithSymbols,
                issues = report.Grounding.Issues,
            },
            coverage = new
            {
                score = report.Coverage.Score,
                resolutionConfidence = report.Coverage.ResolutionConfidence,
                analysisCompleteness = report.Coverage.AnalysisCompleteness,
                unresolvedDispatchCount = report.Coverage.UnresolvedDispatchCount,
                issues = report.Coverage.Issues,
            },
            calibration = new
            {
                score = report.Calibration.Score,
                isOverConfident = report.Calibration.IsOverConfident,
                claimedConfidence = report.Calibration.ClaimedConfidence,
                evidenceBasedConfidence = report.Calibration.EvidenceBasedConfidence,
                issues = report.Calibration.Issues,
            },
            hypotheses = new
            {
                score = report.Hypotheses.Score,
                competingCount = report.Hypotheses.CompetingHypothesisCount,
                hasPlausibleAlternatives = report.Hypotheses.HasPlausibleAlternatives,
                alternatives = report.Hypotheses.Alternatives,
                issues = report.Hypotheses.Issues,
            },
            utility = new
            {
                score = report.Utility.Score,
                isActionable = report.Utility.IsActionable,
                hasEngineeringGuidance = report.Utility.HasEngineeringGuidance,
                issues = report.Utility.Issues,
            },
        };
    }

    private object SelfCritique(Dictionary<string, JsonElement> args)
    {
        EnsureSession();
        var question = GetStringArg(args, "question");
        var result = _session!.Query(question);

        var epistemic = new EpistemicBoundary(_session.QueryService!).Analyze(result, question);
        var evaluator = new ResponseSelfEvaluator();
        var evaluation = evaluator.Evaluate(result, epistemic);
        var riskAnalyzer = new EpistemicRiskAnalyzer();
        var riskReport = riskAnalyzer.Analyze(result, epistemic);
        var gapDetector = new InvestigationGapDetector();
        var gapReport = gapDetector.Detect(result, epistemic);
        var critiqueGen = new SelfCritiqueGenerator();
        var critique = critiqueGen.Generate(evaluation, riskReport, gapReport, result);

        return new
        {
            question,
            critiqueId = critique.CritiqueId,
            honestyStatement = critique.OverallHonestyStatement,
            isHighQuality = critique.IsHighQuality,
            weaknesses = critique.Weaknesses,
            unknowns = critique.Unknowns,
            confidenceReductions = critique.ConfidenceReductions,
            evaluation = new
            {
                overallScore = Math.Round(evaluation.OverallScore, 3),
                groundingScore = Math.Round(evaluation.GroundingScore, 3),
                epistemicHonestyScore = Math.Round(evaluation.EpistemicHonestyScore, 3),
                completenessScore = Math.Round(evaluation.CompletenessScore, 3),
                precisionScore = Math.Round(evaluation.PrecisionScore, 3),
                contradictionScore = Math.Round(evaluation.ContradictionScore, 3),
                qualitySummary = evaluation.QualitySummary,
                passesQualityThreshold = evaluation.PassesQualityThreshold,
                recommendedAction = evaluation.RecommendedAction.ToString(),
                findings = evaluation.Findings.Select(f => new { kind = f.Kind.ToString(), description = f.Description }).ToList(),
            },
        };
    }

    private object EpistemicBoundary(Dictionary<string, JsonElement> args)
    {
        EnsureSession();
        var question = GetStringArg(args, "question");
        var result = _session!.Query(question);

        var epistemic = new Core.Cognition.Epistemics.EpistemicBoundary(_session.QueryService!);
        var report = epistemic.Analyze(result, question);

        return new
        {
            question,
            reportId = report.ReportId,
            canPresentAsDefinitiveConclusion = report.CanPresentAsDefinitiveConclusion,
            searchedNodeCount = report.SearchedNodeCount,
            totalNetworkSize = report.TotalNetworkSize,
            groundedPresentCount = report.GroundedPresentCount,
            groundedAbsentCount = report.GroundedAbsentCount,
            unresolvedCount = report.UnresolvedCount,
            incompleteCount = report.IncompleteCount,
            notSearchedCount = report.NotSearchedCount,
            confidence = new
            {
                entityResolution = Math.Round(report.Confidence.EntityResolution, 3),
                impactAnalysis = Math.Round(report.Confidence.ImpactAnalysis, 3),
                runtimeCompleteness = Math.Round(report.Confidence.RuntimeCompleteness, 3),
                isHighConfidence = report.Confidence.IsHighConfidence,
            },
            annotations = report.Annotations.Select(a => new
            {
                subject = a.Subject,
                evidenceState = a.EvidenceState.ToString(),
                explanation = a.Explanation,
                confidence = a.Confidence.ToString(),
            }).ToList(),
            coverageGaps = report.CoverageGaps.Select(g => new
            {
                gapType = g.GapType,
                description = g.Description,
                suggestedAction = g.SuggestedAction,
                severity = g.Severity.ToString(),
            }).ToList(),
        };
    }

    private object Patch(Dictionary<string, JsonElement> args)
    {
        EnsureSession();
        var request = GetStringArg(args, "request");

        var conventionAnalyzer = new ConventionAnalyzer(_session!.QueryService!);
        var planner = new PatchPlanner(
            _session.QueryService!, conventionAnalyzer,
            _session.ArchitectureExplorer!, _session.ImpactAnalyzer!);
        var generator = new GroundedPatchGenerator();
        var validator = new PatchImpactValidator(_session.QueryService!, _session.ConfidenceEngine!);
        var etp = new ExplainThenPatch(planner, generator, validator);

        var result = etp.ExplainAndPatch(request, _session.RepositoryName);

        return new
        {
            request,
            resultId = result.ResultId,
            overallConfidence = result.OverallConfidence.ToString(),
            explanation = result.Explanation,
            plan = new
            {
                planId = result.Plan.PlanId,
                strategy = result.Plan.Strategy,
                planConfidence = result.Plan.PlanConfidence.ToString(),
                modificationPoints = result.Plan.ModificationPoints.Select(m => new
                {
                    targetNodeId = m.TargetNodeId,
                    targetLabel = m.TargetLabel,
                    targetKind = m.TargetKind,
                    sourceFile = m.SourceFile,
                    reason = m.Reason,
                    affectedCallers = m.AffectedCallers,
                    affectedCallees = m.AffectedCallees,
                    confidence = m.Confidence.ToString(),
                }).ToList(),
                impactedServices = result.Plan.ImpactedServices,
            },
            validation = new
            {
                reportId = result.Validation.ReportId,
                isSafe = result.Validation.IsSafe,
                overallRisk = result.Validation.OverallRisk.ToString(),
                riskFactors = result.Validation.RiskFactors,
                validatedPaths = result.Validation.ValidatedPaths,
            },
            patches = result.Patches.Select(p => new
            {
                patchId = p.PatchId,
                targetFile = p.TargetFile,
                description = p.Description,
                generatedCode = p.GeneratedCode,
                explanation = p.Explanation,
                confidence = p.Confidence.ToString(),
                conventionsApplied = p.ConventionsApplied,
            }).ToList(),
        };
    }

    // ── Helpers ──

    private void EnsureSession()
    {
        if (_session is null || !_session.IsLoaded)
            throw new InvalidOperationException("认知工具需要加载仓库。请先使用 ContextEngine 加载代码库。");
    }

    private static object SerializeCognitionResult(CognitionResult result)
    {
        return new
        {
            resultId = result.ResultId,
            query = result.Query,
            resultType = result.ResultType.ToString(),
            overallConfidence = result.OverallConfidence.ToString(),
            evidenceCount = result.EvidenceCount,
            explanations = result.Explanations.Select(e => new
            {
                text = e.Text,
                claim = e.Claim,
                confidenceLevel = e.ConfidenceLevel.ToString(),
                supportingNodeIds = e.SupportingNodeIds,
                supportingSourceFiles = e.SupportingSourceFiles,
            }).ToList(),
            citations = result.Citations.Select(c => new
            {
                sourceNodeId = c.SourceNodeId,
                sourceNodeLabel = c.SourceNodeLabel,
                sourceFile = c.SourceFile,
                symbolHandle = c.SymbolHandle,
                confidenceLevel = c.ConfidenceLevel.ToString(),
                edgeKind = c.EdgeKind,
                layer = c.Layer,
            }).ToList(),
        };
    }

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
        new("ce_ask", "Ask a natural language engineering question about the loaded codebase. The query is automatically routed to the most appropriate cognition engine (architecture explorer, impact analyzer, capability mapper, or root cause explorer). Returns grounded, citation-backed analysis with evidence references.")
        {
            Parameters =
            {
                new("question", "string", true, "Natural language question about the codebase, e.g. '解释支付架构' or '改动 RetryPolicy 会有什么影响?'"),
            },
        },
        new("ce_verify", "Verify the trustworthiness of a cognition result. Runs 5 verifiers (grounding, coverage, calibration, hypotheses, utility) and returns a detailed trustworthiness verdict. Use this after ce_ask to check if the answer is reliable.")
        {
            Parameters =
            {
                new("question", "string", true, "The same question used in ce_ask to verify"),
            },
        },
        new("ce_self_critique", "Generate an honest system self-critique of a cognition response. Identifies weaknesses, unknowns, and reasons to reduce confidence. Produces a self-evaluation across 5 dimensions (grounding, epistemic honesty, completeness, precision, contradiction).")
        {
            Parameters =
            {
                new("question", "string", true, "The question to self-critique the answer for"),
            },
        },
        new("ce_epistemic_boundary", "Analyze the epistemic boundary of a cognition result: what evidence states exist (grounded present, grounded absent, unresolved dispatch, incomplete analysis, not searched), coverage gaps, and whether the result can be presented as a definitive conclusion.")
        {
            Parameters =
            {
                new("question", "string", true, "The question to analyze epistemic boundaries for"),
            },
        },
        new("ce_patch", "Generate a grounded code patch for a natural language modification request. Explains what to change and why, then produces code suggestions grounded in the codebase's conventions (naming, DI, logging, async patterns). Returns explanation, modification plan, impact validation, and generated patches.")
        {
            Parameters =
            {
                new("request", "string", true, "Natural language modification request, e.g. '给 PaymentService 添加重试逻辑' or '为 UserController 增加日志'"),
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

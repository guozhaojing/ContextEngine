// =============================================================================
// WebApi/WebApiEndpoints.cs — all API endpoints, unified through MCP tools layer
// =============================================================================
// Cognition endpoints delegate to ContextEngineMcpTools (same backend as MCP).
// Session/infrastructure endpoints remain in WebApiSessionManager.
// =============================================================================
using System.Text.Json;
using System.Text.Json.Serialization;
using App.Mcp;
using Core.Cognition.CodeFix;
using Core.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace App.WebApi;

public static class WebApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static ContextEngineMcpTools? GetTools(WebApiSessionManager sm)
    {
        var session = sm.Session;
        if (session?.QueryService is null) return null;
        return new ContextEngineMcpTools(session.QueryService, session);
    }

    public static void Map(WebApplication app, WebApiSessionManager sessionManager)
    {
        // ── Session & repository ──
        app.MapGet("/api/session", () => Results.Json(sessionManager.GetSessionInfo(), JsonOptions));

        app.MapPost("/api/load", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<LoadRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Path))
                return Results.BadRequest(new { error = "路径不能为空" });
            var result = sessionManager.StartLoad(req.Path, req.ForceReload);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });
            return Results.Json(new { loading = true, name = result.RepositoryName }, JsonOptions);
        });

        app.MapGet("/api/load/status", () => Results.Json(sessionManager.GetLoadStatus(), JsonOptions));

        app.MapPost("/api/reload", () =>
        {
            if (sessionManager.CurrentPath is null) return Results.BadRequest(new { error = "请先加载仓库" });
            if (sessionManager.IsLoading) return Results.BadRequest(new { error = "加载已在进行中" });
            _ = sessionManager.StartLoad(sessionManager.CurrentPath, forceReload: true);
            return Results.Json(new { loading = true }, JsonOptions);
        });

        app.MapPost("/api/cache/clear", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<LoadRequest>(JsonOptions);
            return Results.Json(new { message = sessionManager.ClearCache(req?.Path) }, JsonOptions);
        });

        // ── Unified Agent → delegates to ContextEngineMcpTools ──
        app.MapPost("/api/agent", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<AgentRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Message))
                return Results.BadRequest(new { error = "输入不能为空" });
            if (!sessionManager.IsLoaded)
                return Results.BadRequest(new { error = "请先加载仓库" });

            var msg = req.Message.Trim();
            var intent = DetectIntent(msg);

            if (intent == AgentIntent.CodeFix && !string.IsNullOrWhiteSpace(req.ApiKey))
                return await HandleCodeFix(sessionManager, msg, req);

            // Cognition query — delegate to MCP tools layer
            var tools = GetTools(sessionManager);
            if (tools is null) return Results.BadRequest(new { error = "会话未就绪" });

            var result = tools.Ask(msg);
            return Results.Json(result, JsonOptions);
        });

        // ── Cognition endpoints (unified through MCP tools layer) ──
        app.MapPost("/api/cognition/ask", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<QueryRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Question))
                return Results.BadRequest(new { error = "问题不能为空" });
            var tools = GetTools(sessionManager);
            if (tools is null) return Results.BadRequest(new { error = "请先加载仓库" });
            return Results.Json(tools.Ask(req.Question), JsonOptions);
        });

        app.MapPost("/api/cognition/verify", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<QueryRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Question))
                return Results.BadRequest(new { error = "问题不能为空" });
            var tools = GetTools(sessionManager);
            if (tools is null) return Results.BadRequest(new { error = "请先加载仓库" });
            return Results.Json(tools.Verify(req.Question), JsonOptions);
        });

        app.MapPost("/api/cognition/self-critique", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<QueryRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Question))
                return Results.BadRequest(new { error = "问题不能为空" });
            var tools = GetTools(sessionManager);
            if (tools is null) return Results.BadRequest(new { error = "请先加载仓库" });
            return Results.Json(tools.SelfCritique(req.Question), JsonOptions);
        });

        app.MapPost("/api/cognition/epistemic-boundary", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<QueryRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Question))
                return Results.BadRequest(new { error = "问题不能为空" });
            var tools = GetTools(sessionManager);
            if (tools is null) return Results.BadRequest(new { error = "请先加载仓库" });
            return Results.Json(tools.EpistemicBoundary(req.Question), JsonOptions);
        });

        app.MapPost("/api/cognition/patch", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<PatchRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Request))
                return Results.BadRequest(new { error = "修改请求不能为空" });
            var tools = GetTools(sessionManager);
            if (tools is null) return Results.BadRequest(new { error = "请先加载仓库" });
            return Results.Json(tools.Patch(req.Request), JsonOptions);
        });

        // ── Observability ──
        app.MapGet("/api/observability/map", () =>
        {
            var s = sessionManager.MapGenerator.GenerateSubsystemMap().Subsystems
                .Select(s => new { name = s.Name, layer = s.Layer, purpose = s.Purpose, dependsOn = s.DependsOn, usedBy = s.UsedBy });
            return Results.Json(s, JsonOptions);
        });
        app.MapGet("/api/observability/pipeline", () => Results.Content(sessionManager.MapGenerator.GeneratePipelineMap(), "text/plain; charset=utf-8"));
        app.MapGet("/api/observability/health", () =>
        {
            var r = sessionManager.Complexity.Analyze();
            return Results.Json(new { isHealthy = r.IsHealthy, overallRisk = r.OverallRisk.ToString(), findings = r.Findings.Select(f => new { f.Category, f.Description, severity = f.Severity.ToString(), f.Recommendation }) }, JsonOptions);
        });

        // ── Repository history ──
        app.MapGet("/api/history", () => Results.Json(sessionManager.GetHistory(), JsonOptions));
        app.MapDelete("/api/history", async (HttpRequest httpReq) =>
        {
            var req = await httpReq.ReadFromJsonAsync<HistoryDeleteRequest>(JsonOptions);
            if (req is null || string.IsNullOrWhiteSpace(req.Path)) return Results.BadRequest(new { error = "path required" });
            return Results.Json(new { removed = sessionManager.RemoveHistory(req.Path) });
        });
        app.MapDelete("/api/history/all", () => { sessionManager.ClearAllHistory(); return Results.Json(new { cleared = true }); });

        // ── Evidence ──
        app.MapGet("/api/evidence/{nodeId}", (string nodeId) =>
        {
            if (sessionManager.Session?.GraphIndex is null || sessionManager.Session.QueryService is null) return Results.NotFound();
            var node = sessionManager.Session.QueryService.GetNode(nodeId);
            if (node is null) return Results.NotFound();
            return Results.Json(new
            {
                nodeId = node.Id, label = node.Label, kind = node.Kind, sourceFile = node.SourceFile,
                symbolHandle = node.SymbolHandle, className = node.ClassName, namespaceName = node.Namespace,
                groundingKind = node.GroundingKind, truthType = node.TruthType, confidence = node.Confidence,
                callers = sessionManager.Session.QueryService.GetCallers(nodeId),
                callees = sessionManager.Session.QueryService.GetCallees(nodeId),
                callerCount = sessionManager.Session.QueryService.GetCallers(nodeId).Count,
                calleeCount = sessionManager.Session.QueryService.GetCallees(nodeId).Count,
            }, JsonOptions);
        });
    }

    // ── Agent helpers ──

    private static AgentIntent DetectIntent(string msg)
    {
        var lower = msg.ToLowerInvariant();
        if (lower.Contains("修改") || lower.Contains("修复") || lower.Contains("加") && lower.Contains("代码")
            || lower.Contains("改成") || lower.Contains("增加") || lower.Contains("fix") || lower.Contains("patch")
            || lower.Contains("实现") || lower.Contains("改"))
            return AgentIntent.CodeFix;
        return AgentIntent.Query;
    }

    private static async Task<IResult> HandleCodeFix(WebApiSessionManager sessionManager, string msg, AgentRequest req)
    {
        // Extract method name from message (first PascalCase word)
        var words = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var methodName = words.FirstOrDefault(w => w.Length > 2 && char.IsUpper(w[0]) && !w.Contains("修改", StringComparison.Ordinal) && !w.Contains("修复", StringComparison.Ordinal))
            ?? words.LastOrDefault(w => w.Length > 2);

        var llmConfig = new LlmConfig
        {
            BaseUrl = req.ApiBaseUrl ?? "https://api.openai.com",
            Model = req.Model ?? "gpt-4o",
            ApiKey = req.ApiKey ?? "",
        };

        var llmProvider = new LlmProvider(llmConfig);
        var pipeline = new CodeFixPipeline(sessionManager.Session!.QueryService!,
            new PipelineOptions { ProjectPath = req.ProjectPath ?? sessionManager.CurrentPath },
            sessionManager.Session.SemanticSearch);

        var request = new CodeFixRequest
        {
            Query = methodName ?? msg,
            Task = msg,
            TargetMethodName = methodName,
            RepositoryPath = req.ProjectPath ?? sessionManager.CurrentPath,
            MaxRetries = 2,
        };

        var result = await pipeline.ExecuteAsync(request, llmProvider.GenerateAsync);
        return Results.Json(new
        {
            type = "codefix",
            success = result.Success,
            attempts = result.Attempts,
            summary = result.Summary,
            repairHistory = result.RepairHistory,
            patches = result.Patches.Select(p => new { p.FilePath, p.ChangeDescription, diff = p.Diff }),
            buildErrors = result.FinalBuild?.Errors.Select(e => $"{e.Code}: {e.Message}"),
        }, JsonOptions);
    }
}

public enum AgentIntent { Query = 0, CodeFix = 1 }

public sealed class AgentRequest
{
    public string Message { get; set; } = "";
    public string? ApiBaseUrl { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? ProjectPath { get; set; }
}

public sealed class LoadRequest { public string Path { get; set; } = ""; public bool ForceReload { get; set; } }
public sealed class QueryRequest { public string Question { get; set; } = ""; }
public sealed class PatchRequest { public string Request { get; set; } = ""; }
public sealed class HistoryDeleteRequest { public string Path { get; set; } = ""; }

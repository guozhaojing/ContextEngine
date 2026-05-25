// =============================================================================
// Cli/CognitionRepl.cs — interactive developer cognition REPL
// =============================================================================
// Determinism: command handling is stateless; same query sequence → same output.
// Provenance: every interaction is recorded in the investigation session.
// Replay: session history can be exported for regression.
// Grounding: all cognition output is grounded and citation-backed.
// =============================================================================
// Cli/CognitionRepl.cs — interactive developer cognition REPL
// =============================================================================
using System.Diagnostics;
using System.Text;
using App.Infrastructure;
using Core.Cognition.Patching;
using Core.Experience;
using Core.Observability;
using Core.SelfValidation;
using Core.Verification;
using Core.Cognition.Epistemics;
using Core.ReasoningUX;

namespace App.Cli;

public sealed class CognitionRepl
{
    private readonly RepositoryLoader _loader;
    private readonly string _cacheDir;
    private RepositorySession? _session;
    private InteractiveCognitionSession? _interactive;
    private bool _running = true;

    private static readonly string[] HelpText =
    {
        "",
        "ContextEngine — 代码认知 CLI",
        "──────────────────────────────────",
        "",
        "命令:",
        "  load <路径>              加载 .NET 解决方案或项目",
        "  reload                   重新加载（跳过缓存）",
        "  ask <问题>               自然语言工程问题（自动路由）",
        "  followup <问题>          追问上一个问题",
        "  arch <问题>              强制使用架构探索",
        "  impact <问题>            强制使用变更影响分析",
        "  capability <问题>        强制使用业务能力映射",
        "  debug <问题>             强制使用根因分析",
        "  summary                  显示调查摘要",
        "  history                  显示查询历史",
        "  export <文件.md>         导出调查报告",
        "  cache                    显示缓存状态",
        "  clear                    重置会话上下文",
        "  stats                    显示仓库统计",
        "  help                     显示此帮助",
        "  exit / quit              退出",
        "",
        "示例:",
        "  cognition> load D:\\Projects\\MySolution",
        "  cognition> ask \"解释支付架构\"",
        "  cognition> followup \"谁依赖 PaymentService?\"",
        "  cognition> impact \"改动 RetryPolicy 会有什么影响?\"",
        "  cognition> export 调查报告.md",
        "",
    };

    public CognitionRepl(string cacheDir)
    {
        _cacheDir = cacheDir;
        _loader = new RepositoryLoader(cacheDir);
    }

    public async Task RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.InputEncoding = Encoding.Unicode; } catch { }
        Console.WriteLine();
        Console.WriteLine("ContextEngine — 代码认知 CLI");
        Console.WriteLine("输入 help 查看命令, exit 退出。");
        Console.WriteLine();

        while (_running)
        {
            Console.Write("cognition> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            await ProcessCommand(input.Trim());
        }
    }

    private async Task ProcessCommand(string input)
    {
        try
        {
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || input.Equals("quit", StringComparison.OrdinalIgnoreCase)
                || input.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                _running = false;
                Console.WriteLine("再见。");
                return;
            }

            if (input.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in HelpText)
                    Console.WriteLine(line);
                return;
            }

            if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                _interactive = _session is not null
                    ? new InteractiveCognitionSession(_session)
                    : null;
                Console.WriteLine("上下文已清除，开始新会话。");
                return;
            }

            if (input.Equals("summary", StringComparison.OrdinalIgnoreCase))
            {
                ShowSummary();
                return;
            }

            if (input.Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                ShowHistory();
                return;
            }

            if (input.Equals("stats", StringComparison.OrdinalIgnoreCase))
            {
                ShowStats();
                return;
            }

            if (input.Equals("cache", StringComparison.OrdinalIgnoreCase))
            {
                ShowCacheStatus();
                return;
            }

            if (input.Equals("map", StringComparison.OrdinalIgnoreCase))
            {
                var mapGen = new SystemMapGenerator();
                Console.WriteLine(mapGen.GeneratePipelineMap());
                return;
            }

            if (input.Equals("health", StringComparison.OrdinalIgnoreCase))
            {
                var mapGen = new SystemMapGenerator();
                var analyzer = new ComplexityAnalyzer(mapGen);
                var result = analyzer.Analyze();
                Console.WriteLine(result.IsHealthy ? "架构健康" : $"需关注 — {result.Findings.Count} 个发现");
                foreach (var f in result.Findings)
                    Console.WriteLine($"  [{f.Severity}] {f.Category}: {f.Description}");
                return;
            }

            if (input.StartsWith("verify ", StringComparison.OrdinalIgnoreCase))
            {
                EnsureSessionLoaded();
                var q = input[7..].Trim().Trim('"');
                var result = _session!.Query(q);
                if (result is not null)
                {
                    var verifier = new VerificationOrchestrator();
                    var epistemic = new EpistemicBoundary(_session.QueryService!).Analyze(result, q);
                    var report = verifier.Verify(result, epistemic);
                    Console.WriteLine();
                    Console.WriteLine($"可信度裁定: {report.Verdict}");
                    Console.WriteLine(report.Summary);
                }
                return;
            }

            if (input.StartsWith("self-critique ", StringComparison.OrdinalIgnoreCase))
            {
                EnsureSessionLoaded();
                var q = input[14..].Trim().Trim('"');
                var result = _session!.Query(q);
                if (result is not null)
                {
                    var epistemic = new EpistemicBoundary(_session.QueryService!).Analyze(result, q);
                    var evaluator = new ResponseSelfEvaluator();
                    var evaluation = evaluator.Evaluate(result, epistemic);
                    var riskAnalyzer = new EpistemicRiskAnalyzer();
                    var riskReport = riskAnalyzer.Analyze(result, epistemic);
                    var gapDetector = new InvestigationGapDetector();
                    var gapReport = gapDetector.Detect(result, epistemic);
                    var critiqueGen = new SelfCritiqueGenerator();
                    var critique = critiqueGen.Generate(evaluation, riskReport, gapReport, result);
                    Console.WriteLine();
                    Console.WriteLine(critique.GenerateCritiqueText());
                }
                return;
            }

            if (input.StartsWith("patch ", StringComparison.OrdinalIgnoreCase))
            {
                EnsureSessionLoaded();
                var request = input[6..].Trim().Trim('"');
                var conventionAnalyzer = new ConventionAnalyzer(_session!.QueryService!);
                var planner = new PatchPlanner(_session.QueryService!, conventionAnalyzer,
                    _session.ArchitectureExplorer!, _session.ImpactAnalyzer!);
                var generator = new GroundedPatchGenerator();
                var validator = new PatchImpactValidator(_session.QueryService!, _session.ConfidenceEngine!);
                var etp = new ExplainThenPatch(planner, generator, validator);
                var patchResult = etp.ExplainAndPatch(request, _session.RepositoryName);
                Console.WriteLine();
                Console.WriteLine(patchResult.Explanation);
                return;
            }

            if (input.StartsWith("load ", StringComparison.OrdinalIgnoreCase))
            {
                var path = input[5..].Trim().Trim('"');
                await LoadRepository(path);
                return;
            }

            if (input.Equals("reload", StringComparison.OrdinalIgnoreCase))
            {
                if (_session is not null)
                {
                    _loader.InvalidateCache(_session.RepositoryPath);
                    await LoadRepository(_session.RepositoryPath);
                }
                else
                {
                    Console.WriteLine("未加载仓库。请先用 'load <路径>'。");
                }
                return;
            }

            if (input.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                var filePath = input[7..].Trim().Trim('"');
                await ExportReport(filePath);
                return;
            }

            if (input.StartsWith("ask ", StringComparison.OrdinalIgnoreCase))
            {
                var question = input[4..].Trim().Trim('"');
                AskQuestion(question);
                return;
            }

            if (input.StartsWith("followup ", StringComparison.OrdinalIgnoreCase))
            {
                var question = input[9..].Trim().Trim('"');
                FollowUp(question);
                return;
            }

            if (input.StartsWith("arch ", StringComparison.OrdinalIgnoreCase))
            {
                var q = input[5..].Trim().Trim('"');
                DirectQuery(q, "arch");
                return;
            }

            if (input.StartsWith("impact ", StringComparison.OrdinalIgnoreCase))
            {
                var q = input[7..].Trim().Trim('"');
                DirectQuery(q, "impact");
                return;
            }

            if (input.StartsWith("capability ", StringComparison.OrdinalIgnoreCase))
            {
                var q = input[11..].Trim().Trim('"');
                DirectQuery(q, "capability");
                return;
            }

            if (input.StartsWith("debug ", StringComparison.OrdinalIgnoreCase))
            {
                var q = input[6..].Trim().Trim('"');
                DirectQuery(q, "debug");
                return;
            }

            Console.WriteLine($"未知命令: {input.Split(' ')[0]}");
            Console.WriteLine("输入 'help' 查看可用命令。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private async Task LoadRepository(string path)
    {
        await LoadRepositoryInternal(path);
    }

    public async Task LoadRepositoryFromArgs(string path)
    {
        await LoadRepositoryInternal(path);
    }

    private async Task LoadRepositoryInternal(string path)
    {
        Console.WriteLine($"正在加载: {path}");
        var sw = Stopwatch.StartNew();

        var result = await _loader.LoadAsync(path);
        if (!result.Success)
        {
            Console.WriteLine(result.Error);
            return;
        }

        Console.WriteLine(result.IsFromCache ? "  从缓存加载。" : $"  扫描完成: {result.NodeCount} 个节点, {result.EdgeCount} 条边。");

        var buildResult = result.BuildResult;

        var sessionConfig = new RepositorySessionConfig
        {
            RepositoryPath = path,
            RepositoryName = result.RepositoryName,
        };

        _session = new RepositorySession(sessionConfig);
        _session.Load(buildResult);

        _interactive = new InteractiveCognitionSession(_session);

        sw.Stop();
        Console.WriteLine($"仓库已加载。({sw.Elapsed.TotalSeconds:F1}秒)");
        Console.WriteLine($"  项目:      {result.ProjectCount}");
        Console.WriteLine($"  节点:      {result.NodeCount}");
        Console.WriteLine($"  边:        {result.EdgeCount}");
        Console.WriteLine($"  事实:      {result.FactCount}");
        Console.WriteLine();
        Console.WriteLine("就绪。试试: ask \"解释架构\"");
    }

    private void AskQuestion(string question)
    {
        EnsureSessionLoaded();

        if (_interactive is null)
        {
            _interactive = new InteractiveCognitionSession(_session!);
        }

        var response = _interactive.Ask(question);
        Console.WriteLine();
        Console.WriteLine(response.FormattedResponse);
        Console.WriteLine();

        if (response.SuggestedFollowUps.Count > 0)
        {
            Console.WriteLine("建议追问:");
            foreach (var s in response.SuggestedFollowUps.Take(3))
                Console.WriteLine($"  → {s}");
            Console.WriteLine();
        }
    }

    private void FollowUp(string question)
    {
        EnsureSessionLoaded();

        if (_interactive is null)
        {
            Console.WriteLine("无活跃会话。请先使用 'ask'。");
            return;
        }

        var response = _interactive.FollowUp(question);
        Console.WriteLine();
        Console.WriteLine(response.FormattedResponse);
        Console.WriteLine();

        if (response.SuggestedFollowUps.Count > 0)
        {
            Console.WriteLine("建议追问:");
            foreach (var s in response.SuggestedFollowUps.Take(3))
                Console.WriteLine($"  → {s}");
            Console.WriteLine();
        }
    }

    private void DirectQuery(string question, string engine)
    {
        EnsureSessionLoaded();

        var result = engine switch
        {
            "arch" => _session!.ExploreArchitecture(question),
            "impact" => _session!.AnalyzeImpact(question),
            "capability" => _session!.MapCapabilities(question),
            "debug" => _session!.ExploreRootCause(question),
            _ => _session!.ExploreArchitecture(question),
        };

        Console.WriteLine();
        Console.WriteLine(_session!.Formatter!.Format(result));
        Console.WriteLine();
    }

    private void ShowSummary()
    {
        if (_interactive is null)
        {
            Console.WriteLine("无活跃会话。");
            return;
        }

        var summary = _interactive.Summarize();
        Console.WriteLine();
        Console.WriteLine(summary.Format());
    }

    private void ShowHistory()
    {
        if (_interactive is null)
        {
            Console.WriteLine("无活跃会话。");
            return;
        }

        var history = _interactive.History;
        Console.WriteLine();

        if (history.Count == 0)
        {
            Console.WriteLine("暂无查询记录。");
            return;
        }

        for (var i = 0; i < history.Count; i++)
        {
            var entry = history[i];
            var conf = entry.Result.OverallConfidence;
            var count = entry.Result.EvidenceCount;
            Console.WriteLine($"  {i + 1}. [{entry.RoutedTo}] {entry.Question}");
            Console.WriteLine($"     confidence={conf} evidence={count}");
        }
        Console.WriteLine();
    }

    private void ShowStats()
    {
        if (_session is null || !_session.IsLoaded)
        {
            Console.WriteLine("未加载仓库。");
            return;
        }

        var snap = _session.Snapshot();
        Console.WriteLine();
        Console.WriteLine($"仓库:     {snap.RepositoryName}");
        Console.WriteLine($"路径:     {snap.RepositoryPath}");
        Console.WriteLine($"节点:     {snap.NodeCount}");
        Console.WriteLine($"边:       {snap.EdgeCount}");
        Console.WriteLine($"事实:     {snap.FactCount}");
        Console.WriteLine($"查询次数: {snap.TotalQueries}");
        Console.WriteLine();
    }

    private void ShowCacheStatus()
    {
        if (_session is null || !_session.IsLoaded)
        {
            Console.WriteLine("未加载仓库。");
            return;
        }

        var info = _loader.Cache.GetInfo(_session.RepositoryPath);
        if (info is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"缓存:    {info.RepositoryPath}");
            Console.WriteLine($"缓存于:  {info.CachedAt}");
            Console.WriteLine($"节点:    {info.NodeCount}");
            Console.WriteLine($"边:      {info.EdgeCount}");
            Console.WriteLine($"事实:    {info.FactCount}");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("无缓存。");
        }
    }

    private async Task ExportReport(string filePath)
    {
        if (_interactive is null)
        {
            Console.WriteLine("无活跃会话可导出。");
            return;
        }

        if (!filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            filePath += ".md";

        var sb = new StringBuilder();
        sb.AppendLine($"# ContextEngine 调查报告");
        sb.AppendLine($"生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();

        if (_session is not null)
        {
            var snap = _session.Snapshot();
            sb.AppendLine($"## 仓库信息");
            sb.AppendLine($"- 名称: {snap.RepositoryName}");
            sb.AppendLine($"- 路径: {snap.RepositoryPath}");
            sb.AppendLine($"- 节点: {snap.NodeCount}");
            sb.AppendLine($"- 边: {snap.EdgeCount}");
            sb.AppendLine();
        }

        sb.AppendLine("## 会话历史");
        sb.AppendLine();

        foreach (var entry in _interactive.History)
        {
            sb.AppendLine($"### Q{entry.SequenceIndex + 1}: {entry.Question}");
            sb.AppendLine($"*Engine: {entry.RoutedTo} | Confidence: {entry.Result.OverallConfidence}*");
            sb.AppendLine();
            sb.AppendLine(entry.Result.Format());
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        var summary = _interactive.Summarize();
        sb.AppendLine("## Summary");
        sb.AppendLine(summary.Format());

        await File.WriteAllTextAsync(filePath, sb.ToString());
        Console.WriteLine($"报告已导出: {Path.GetFullPath(filePath)}");
    }

    private void EnsureSessionLoaded()
    {
        if (_session is null || !_session.IsLoaded)
            throw new InvalidOperationException("未加载仓库。请先用 'load <路径>'。");
    }
}

// =============================================================================
// Program.cs — multi-mode: REPL + scan + Web API + enterprise + benchmark
// =============================================================================
// Usage:
//   dotnet run --enterprise <path>     → enterprise semantic recovery (JSON out)
//   dotnet run                         → interactive cognition REPL
//   dotnet run --scan <path>           → legacy full pipeline scan mode
//   dotnet run --load <path>           → load repo and enter REPL immediately
//   dotnet run --web                   → start Web API + cognition server
//   dotnet run --benchmark <path>      → semantic retrieval benchmark
// =============================================================================

using System.Text;
using App.Cli;
using App.WebApi;
using Core.Cognition.SemanticDoc;
using Core.Enterprise;
using Core.Export;
using Core.Graph;
using Core.Graph.Analysis;
using Core.Graph.Analysis.Analyzers;
using Core.Graph.Analysis.GenericResolution;
using Core.Graph.Query;
using Core.Retrieval.Embedding;
using Core.Retrieval.Evaluation;
using Core.Retrieval.Retrieval;
using Core.Retrieval.VectorStore;
using Core.Scanning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

try { Console.OutputEncoding = Encoding.UTF8; } catch { }

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToList();

if (cliArgs.Contains("--enterprise") || cliArgs.Contains("-e"))
{
    RunEnterpriseMode(cliArgs);
}
else if (cliArgs.Contains("--web") || cliArgs.Contains("-w"))
{
    await RunWebApiMode();
}
else if (cliArgs.Contains("--benchmark") || cliArgs.Contains("-b"))
{
    await RunBenchmarkMode(cliArgs);
}
else if (cliArgs.Contains("--scan") || cliArgs.Contains("-s"))
{
    await RunLegacyScanMode(cliArgs);
}
else
{
    await RunCognitionReplMode(cliArgs);
}

return 0;

// ═══════════════════════════════════════════════════════════════
// Web API Mode
// ═══════════════════════════════════════════════════════════════

static async Task RunWebApiMode()
{
    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        });
    });

    var app = builder.Build();
    app.UseCors();

    var cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ContextEngine", "cache");
    Directory.CreateDirectory(cacheDir);

    // Serve React frontend FIRST (before API routes)
    var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    if (Directory.Exists(wwwroot))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        Console.WriteLine($"前端已就绪: {wwwroot}");
    }
    else
    {
        app.MapGet("/", () => Results.Content(
            "<html><body style='font-family:sans-serif;padding:2rem;background:#0f0f1a;color:#ddd'>" +
            "<h1 style='color:#60a5fa'>ContextEngine API</h1>" +
            "<p>API 已就绪。前端未构建，运行: cd webui && npm run build</p>" +
            "</body></html>", "text/html; charset=utf-8"));
    }

    var sessionManager = new WebApiSessionManager(cacheDir);
    WebApiEndpoints.Map(app, sessionManager);

    app.Urls.Add("http://localhost:5290");

    Console.WriteLine("ContextEngine Web API 已启动");
    Console.WriteLine("http://localhost:5290");
    Console.WriteLine();
    Console.WriteLine("前端开发: cd webui && npm run dev");

    await app.RunAsync();
}

// ═══════════════════════════════════════════════════════════════
// Cognition REPL Mode
// ═══════════════════════════════════════════════════════════════

static async Task RunCognitionReplMode(List<string> cliArgs)
{
    var cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ContextEngine", "cache");
    Directory.CreateDirectory(cacheDir);

    var repl = new CognitionRepl(cacheDir);

    var loadIndex = cliArgs.IndexOf("--load");
    if (loadIndex < 0) loadIndex = cliArgs.IndexOf("-l");

    if (loadIndex >= 0 && loadIndex + 1 < cliArgs.Count)
    {
        var path = cliArgs[loadIndex + 1];
            Console.WriteLine($"自动加载: {path}");

            try
            {
                await repl.LoadRepositoryFromArgs(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载失败: {ex.Message}");
        }
    }

    await repl.RunAsync();
}

// ═══════════════════════════════════════════════════════════════
// Legacy Scan Mode (existing functionality)
// ═══════════════════════════════════════════════════════════════

static async Task RunLegacyScanMode(List<string> cliArgs)
{
    Console.WriteLine("ContextEngine — Roslyn 解决方案扫描");
    Console.WriteLine("支持：解决方案目录 / .sln / .csproj（含多层子目录、多项目）");
    Console.WriteLine();

    var scanIndex = cliArgs.IndexOf("--scan");
    if (scanIndex < 0) scanIndex = cliArgs.IndexOf("-s");
    var pathArg = scanIndex >= 0 && scanIndex + 1 < cliArgs.Count ? cliArgs[scanIndex + 1] : null;

    while (true)
    {
        string? scanPath;

        if (pathArg is not null)
        {
            scanPath = Path.GetFullPath(pathArg);
            pathArg = null;
        }
        else
        {
            Console.Write("路径> ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
            {
                scanPath = Directory.GetCurrentDirectory();
            }
            else if (input.Equals("q", StringComparison.OrdinalIgnoreCase)
                || input.Equals("quit", StringComparison.OrdinalIgnoreCase)
                || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            else
            {
                scanPath = Path.GetFullPath(input);
            }
        }

        if (!Directory.Exists(scanPath) && !File.Exists(scanPath))
        {
            Console.WriteLine($"路径不存在: {scanPath}");
            Console.WriteLine();
            continue;
        }

        Console.WriteLine();
        Console.WriteLine($"正在扫描: {scanPath}");
        Console.WriteLine();

        try
        {
            var scanner = new ProjectCodeScanner();
            var scan = await scanner.ScanAsync(scanPath);
            var scanOutputPath = await CodeUnitJsonExporter.SaveAsync(scan);

            var nhibernateAnalyzer = new NHibernateAnalyzer();
            var genericAnalyzer = new NhSessionGenericAnalyzer();
            var graphOrchestrator = new CodeGraphAnalysisOrchestrator(new IGraphAnalyzer[]
            {
                new AspNetRouteAnalyzer(),
                new SpringBeanAnalyzer(),
                new SpringContextObjectAnalyzer(),
                nhibernateAnalyzer,
                genericAnalyzer
            });
            var graphBuild = graphOrchestrator.BuildAndAnalyze(scan);
            var graphPath = await CodeGraphJsonExporter.SaveAsync(graphBuild.Graph);
            var graphQuery = new GraphQueryService(graphBuild);

            Console.WriteLine($"扫描根目录: {scan.ScanRoot}");
            Console.WriteLine($"发现项目:   {scan.Projects.Count}");
            Console.WriteLine($"CodeUnit:   {scan.TotalCodeUnits}");
            Console.WriteLine();
            Console.WriteLine($"代码图节点: {graphBuild.Graph.Nodes.Count}（外部 {graphBuild.Graph.ExternalNodeCount}）");
            Console.WriteLine($"代码图边:   {graphBuild.Graph.Edges.Count}（已解析 {graphBuild.Graph.ResolvedEdgeCount}）");
            Console.WriteLine($"分析事实:   {graphBuild.Graph.Facts.Count}");

            foreach (var g in graphBuild.Graph.Nodes.GroupBy(n => n.Kind).OrderBy(g => g.Key))
                Console.WriteLine($"  节点 [{g.Key}]: {g.Count()}");
            foreach (var g in graphBuild.Graph.Edges.GroupBy(e => e.Kind).OrderBy(g => g.Key))
                Console.WriteLine($"  边 [{g.Key}]: {g.Count()}");

            Console.WriteLine($"代码图文件: {graphPath}");
            Console.WriteLine("完毕。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"扫描失败: {ex.Message}");
        }

        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════
// Enterprise Semantic Recovery Mode
// ═══════════════════════════════════════════════════════════════

static void RunEnterpriseMode(List<string> cliArgs)
{
    var eeIndex = cliArgs.IndexOf("--enterprise");
    if (eeIndex < 0) eeIndex = cliArgs.IndexOf("-e");
    var path = eeIndex >= 0 && eeIndex + 1 < cliArgs.Count ? cliArgs[eeIndex + 1] : null;

    if (path is null)
    {
        Console.WriteLine("用法: dotnet run -- --enterprise <仓库路径> [--output <输出路径>]");
        return;
    }

    var scanRoot = Path.GetFullPath(path);
    if (!Directory.Exists(scanRoot))
    {
        Console.WriteLine($"路径不存在: {scanRoot}");
        return;
    }

    Console.WriteLine($"企业架构语义恢复: {scanRoot}");
    Console.WriteLine();

    var analyzer = new EnterpriseSemanticAnalyzer();
    var result = analyzer.Analyze(scanRoot);

    Console.WriteLine();
    Console.WriteLine($"═══ 恢复结果 ═══");
    Console.WriteLine($"  BLL: {result.Summary.BllCount}");
    Console.WriteLine($"  DAO: {result.Summary.DaoCount}");
    Console.WriteLine($"  Controller: {result.Summary.ControllerCount}");
    Console.WriteLine($"  IOC 绑定: {result.Summary.IocBindingCount}");
    Console.WriteLine($"  查询操作: {result.Summary.QueryOperationCount}");
    Console.WriteLine($"  写操作: {result.Summary.WriteOperationCount}");
    Console.WriteLine($"  调用链: {result.Summary.CallChainCount}");
    Console.WriteLine($"  实体数: {result.Summary.EntityCount}");
    Console.WriteLine();
    Console.WriteLine("实体列表:");
    foreach (var entity in result.Summary.AllEntities)
        Console.WriteLine($"  - {entity}");

    // 输出 IOC 绑定
    Console.WriteLine();
    Console.WriteLine("IOC 绑定:");
    foreach (var binding in result.IocBindings)
        Console.WriteLine($"  {binding.InterfaceName} → {binding.ImplementationName} [{binding.Source}] ({binding.BeanId})");

    // 输出调用链
    Console.WriteLine();
    Console.WriteLine("调用链:");
    foreach (var chain in result.CallChains.Take(20))
    {
        var perm = chain.HasPermissionFilter ? " [权限]" : "";
        Console.WriteLine($"  {chain.ControllerClass}.{chain.ControllerMethod}");
        Console.WriteLine($"    → {chain.BllClass}.{chain.BllMethod} ({chain.OperationType}){perm}");
        if (chain.DaoClass is not null)
            Console.WriteLine($"      → {chain.DaoClass}.{chain.DaoMethod} → [{chain.TargetEntity}]");
    }
    if (result.CallChains.Count > 20)
        Console.WriteLine($"  ... 共 {result.CallChains.Count} 条调用链");

    // 输出 JSON
    var outIdx = cliArgs.IndexOf("--output");
    if (outIdx < 0) outIdx = cliArgs.IndexOf("-o");
    var outPath = outIdx >= 0 && outIdx + 1 < cliArgs.Count
        ? cliArgs[outIdx + 1]
        : Path.Combine(scanRoot, "semantic-recovery.json");

    analyzer.WriteJson(result, outPath);

    var fragDir = Path.Combine(scanRoot, "semantic-index");
    analyzer.WriteFragmentedJson(result, fragDir);
}

// ═══════════════════════════════════════════════════════════════
// Benchmark Mode — run semantic retrieval benchmark
// ═══════════════════════════════════════════════════════════════

static async Task RunBenchmarkMode(List<string> cliArgs)
{
    var benchIdx = cliArgs.IndexOf("--benchmark");
    if (benchIdx < 0) benchIdx = cliArgs.IndexOf("-b");
    var path = benchIdx >= 0 && benchIdx + 1 < cliArgs.Count ? cliArgs[benchIdx + 1] : null;

    if (path is null)
    {
        Console.WriteLine("用法: dotnet run -- --benchmark <仓库路径>");
        return;
    }

    Console.WriteLine($"加载仓库: {path}");
    var scanner = new ProjectCodeScanner();
    var scan = await scanner.ScanAsync(path);

    var orchestrator = new CodeGraphAnalysisOrchestrator(new IGraphAnalyzer[]
    {
        new AspNetRouteAnalyzer(), new SpringBeanAnalyzer(),
        new SpringContextObjectAnalyzer(), new NHibernateAnalyzer(), new NhSessionGenericAnalyzer(),
    });
    var buildResult = orchestrator.BuildAndAnalyze(scan);
    var graphQuery = new GraphQueryService(buildResult);
    Console.WriteLine($"  节点: {buildResult.Graph.Nodes.Count}, 边: {buildResult.Graph.Edges.Count}");

    // Build semantic docs
    Console.WriteLine("构建语义文档...");
    var docBuilder = new SemanticDocBuilder(graphQuery);
    var docResult = docBuilder.BuildAll();
    Console.WriteLine($"  文档: {docResult.DocCount}");

    // Summarize
    var summarizer = new CodeSummarizer();
    summarizer.SummarizeAll(docResult);

    // Embedding index
    Console.WriteLine("生成 Embedding 索引...");
    var provider = new FakeEmbeddingProvider();
    var vectorStore = new InMemoryVectorStore();
    var embeddingService = new SemanticEmbeddingService(provider, vectorStore);
    await embeddingService.IndexAsync(docResult);
    Console.WriteLine($"  索引: {embeddingService.IndexedCount} 向量");

    // Run benchmark
    Console.WriteLine();
    Console.WriteLine("═══ 运行 Semantic Retrieval Benchmark ═══");
    var retrieval = new HybridRetrievalService(embeddingService, graphQuery);
    var runner = new SemanticBenchmarkRunner(retrieval, docResult.ReverseIndex);
    var cases = ZhiFangBenchmarkQueries.Build();
    var result = runner.Run(cases);

    Console.WriteLine(result.GenerateReport());

    // Failure analysis
    Console.WriteLine();
    Console.WriteLine("═══ 失败模式分析 ═══");
    var analyzer = new BenchmarkFailureAnalyzer();
    var failureReport = analyzer.Analyze();
    Console.WriteLine(failureReport.GenerateReport());
}

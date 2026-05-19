// =============================================================================
// Program.cs — dual-mode entry point: legacy scan + cognition REPL
// =============================================================================
// Usage:
//   dotnet run                         → interactive cognition REPL
//   dotnet run --repl                  → interactive cognition REPL (explicit)
//   dotnet run --scan <path>           → legacy full pipeline scan mode
//   dotnet run --load <path>           → load repo and enter REPL immediately
// =============================================================================

using System.Text;
using App.Cli;
using Core.Context;
using Core.Export;
using Core.Export.Dtos;
using Core.Graph;
using Core.Graph.Analysis;
using Core.Graph.Analysis.Analyzers;
using Core.Graph.Analysis.GenericResolution;
using Core.Graph.Query;
using Core.Retrieval.Chunking;
using Core.Retrieval.Embedding;
using Core.Retrieval.Evaluation;
using Core.Retrieval.Explainability;
using Core.Retrieval.Retrieval;
using Core.Retrieval.VectorStore;
using Core.Scanning;

try { Console.OutputEncoding = Encoding.UTF8; } catch { }

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToList();

if (cliArgs.Contains("--scan") || cliArgs.Contains("-s"))
{
    await RunLegacyScanMode(cliArgs);
}
else
{
    await RunCognitionReplMode(cliArgs);
}

return 0;

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

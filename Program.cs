// =============================================================================
// Program.cs — 程序入口（交互式控制台）
// =============================================================================
// 流程：输入路径 → 扫描源码 → 导出 scan.json → 建图+分析 → 导出 graph.json → 示例查询
// =============================================================================

using Core.Export;
using Core.Graph;
using Core.Graph.Analysis;
using Core.Scanning;

Console.WriteLine("ContextEngine — Roslyn 解决方案扫描");
Console.WriteLine("支持：解决方案目录 / .sln / .csproj（含多层子目录、多项目）");
Console.WriteLine("输入路径后回车开始扫描，直接回车使用当前目录，输入 q 退出。");
Console.WriteLine();

// 扫描器：负责读取磁盘上的 C# 项目并产出 CodeUnit
var scanner = new ProjectCodeScanner();

while (true)
{
    Console.Write("路径> ");
    var input = Console.ReadLine()?.Trim();

    // 退出命令
    if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input, "quit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;

    var scanPath = string.IsNullOrEmpty(input)
        ? Directory.GetCurrentDirectory()
        : Path.GetFullPath(input);

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
        // ① 扫描：SyntaxTree + SemanticModel → List<CodeUnit>
        var scan = await scanner.ScanAsync(scanPath);
        var scanOutputPath = await CodeUnitJsonExporter.SaveAsync(scan);

        // ② 建图：基础调用图 +（可选）分析器管道 + 合并
        // Array.Empty<IGraphAnalyzer>() 表示当前没有注册额外分析器
        var graphOrchestrator = new CodeGraphAnalysisOrchestrator(Array.Empty<IGraphAnalyzer>());
        var graphBuild = graphOrchestrator.BuildAndAnalyze(scan);
        var graphPath = await CodeGraphJsonExporter.SaveAsync(graphBuild.Graph);

        // ③ 查询：只读，依赖 GraphIndex，不修改图
        var graphQuery = new GraphQueryService(graphBuild);

        Console.WriteLine($"扫描根目录: {scan.ScanRoot}");
        Console.WriteLine($"发现项目:   {scan.Projects.Count}");
        Console.WriteLine($"CodeUnit:   {scan.TotalCodeUnits}");
        Console.WriteLine($"扫描结果:   {scanOutputPath}");
        Console.WriteLine();
        Console.WriteLine($"代码图节点: {graphBuild.Graph.Nodes.Count}（外部 {graphBuild.Graph.ExternalNodeCount}）");
        Console.WriteLine($"代码图边:   {graphBuild.Graph.Edges.Count}（已解析 {graphBuild.Graph.ResolvedEdgeCount}）");
        Console.WriteLine($"代码图文件: {graphPath}");

        // 演示查询 API
        var sampleNode = graphBuild.Graph.Nodes.FirstOrDefault(n => !n.IsExternal && n.CalledBy.Count > 0)
            ?? graphBuild.Graph.Nodes.FirstOrDefault(n => !n.IsExternal);
        if (sampleNode is not null)
        {
            var entryPoints = graphQuery.FindEntryPoints(sampleNode.Id);
            Console.WriteLine($"查询示例:   {sampleNode.Label}");
            Console.WriteLine($"  上游调用方: {graphQuery.GetCallers(sampleNode.Id).Count}");
            Console.WriteLine($"  下游被调方: {graphQuery.GetCallees(sampleNode.Id).Count}");
            Console.WriteLine($"  入口方法数: {entryPoints.Count}");
            Console.WriteLine($"  调用链(深度2): {graphQuery.GetCallChain(sampleNode.Id, 2).Count} 条");
        }

        Console.WriteLine();

        // 按项目打印每个方法的调用摘要
        foreach (var project in scan.Projects)
        {
            Console.WriteLine($"[{project.ProjectName}] {project.ProjectPath} ({project.CodeUnits.Count})");

            foreach (var unit in project.CodeUnits)
            {
                Console.WriteLine($"  {unit.ClassName}.{unit.MethodName}");
                Console.WriteLine(CodeUnitJsonExporter.FormatOne(unit));
            }

            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"扫描失败: {ex.Message}");
    }

    Console.WriteLine("—".PadRight(50, '—'));
    Console.WriteLine();
}

return 0;

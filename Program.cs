// =============================================================================
// Program.cs — 程序入口（交互式控制台）
// =============================================================================
// 流程：输入路径 → 扫描源码 → 导出 scan.json → 建图+分析 → 导出 graph.json → 示例查询
// =============================================================================

using Core.Export;
using Core.Graph;
using Core.Graph.Analysis;
using Core.Graph.Analysis.Analyzers;
using Core.Graph.Analysis.GenericResolution;
using Core.Graph.Query;
using Core.Graph.Indexing;
using Core.Scanning;
using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

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

        // ② 建图：基础调用图 + 分析器管道 + 合并
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

        // ③ 查询：只读，依赖 GraphIndex，不修改图
        var graphQuery = new GraphQueryService(graphBuild);

        Console.WriteLine($"扫描根目录: {scan.ScanRoot}");
        Console.WriteLine($"发现项目:   {scan.Projects.Count}");
        Console.WriteLine($"CodeUnit:   {scan.TotalCodeUnits}");
        Console.WriteLine($"扫描结果:   {scanOutputPath}");
        Console.WriteLine();
        Console.WriteLine($"代码图节点: {graphBuild.Graph.Nodes.Count}（外部 {graphBuild.Graph.ExternalNodeCount}）");
        Console.WriteLine($"代码图边:   {graphBuild.Graph.Edges.Count}（已解析 {graphBuild.Graph.ResolvedEdgeCount}）");
        Console.WriteLine($"分析事实:   {graphBuild.Graph.Facts.Count}");

        // 按 NodeKind 分组
        foreach (var g in graphBuild.Graph.Nodes.GroupBy(n => n.Kind).OrderBy(g => g.Key))
            Console.WriteLine($"  节点 [{g.Key}]: {g.Count()}");
        // 按 EdgeKind 分组
        foreach (var g in graphBuild.Graph.Edges.GroupBy(e => e.Kind).OrderBy(g => g.Key))
            Console.WriteLine($"  边 [{g.Key}]: {g.Count()}");
        // 按 FactType 分组
        foreach (var g in graphBuild.Graph.Facts.GroupBy(f => f.FactType).OrderBy(g => g.Key))
            Console.WriteLine($"  事实 [{g.Key}]: {g.Count()}");

        Console.WriteLine($"代码图文件: {graphPath}");

        // ── Generic Resolution Benchmark ──────────────────────────
        var genResult = genericAnalyzer.ResolutionResult;
        Console.WriteLine();
        Console.WriteLine("═══ 泛型解析报告 (Generic Resolution) ═══");
        Console.WriteLine($"类扫描数:       {genResult.ClassesScanned}");
        Console.WriteLine($"Repository 类:   {genResult.RepositoryClassesFound}");
        Console.WriteLine($"解析调用数:     {genResult.TotalInvocationsResolved}");
        Console.WriteLine($"发现实体数:     {genResult.DiscoveredEntities.Count}");
        Console.WriteLine($"发现表数:       {genResult.DiscoveredTables.Count}");
        Console.WriteLine($"产出 Edge:      {genResult.EdgesProduced}");
        Console.WriteLine($"产出 Fact:      {genResult.FactsProduced}");
        Console.WriteLine($"产出 Annotation:{genResult.AnnotationsProduced}");

        if (genResult.DiscoveredEntities.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("发现实体列表:");
            foreach (var entity in genResult.DiscoveredEntities)
            {
                var tables = genResult.EntityClassToTableMap
                    .GetValueOrDefault(entity, new List<string>());
                Console.WriteLine($"  {entity} → [{string.Join(", ", tables)}]");
            }
        }

        if (genResult.ResolutionByMethod.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("解析方式分布:");
            foreach (var (method, count) in genResult.ResolutionByMethod
                .OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {method}: {count}");
        }

        Console.WriteLine("════════════════════════════════════");
        Console.WriteLine();

        // 演示查询 API (Query 1.0)
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

        // ── Semantic Query Demo (Query 2.0) ────────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ 语义查询演示 (Query 2.0) ═══");

        // ① 正向遍历：找到一个 Callers>0 的节点，向下看调用链
        var seedNode = graphBuild.Graph.Nodes
            .Where(n => n.Kind == GraphNodeKind.Method && n.CalledBy.Count > 0)
            .OrderByDescending(n => n.CalledBy.Count)
            .FirstOrDefault();
        if (seedNode is not null)
        {
            Console.WriteLine($"① 正向遍历 [{seedNode.Label}]");
            Console.WriteLine($"   CalledBy: {seedNode.CalledBy.Count}");

            var fwdPaths = SemanticTraversalEngine.Traverse(
                graphBuild.Index,
                new[] { seedNode.Id },
                new SemanticTraversalOptions
                {
                    EdgeKinds = new HashSet<string>(StringComparer.Ordinal) { "call" },
                    Direction = TraversalDirection.Forward,
                    MaxDepth = 2,
                    MaxPaths = 5
                });

            Console.WriteLine($"   路径: {fwdPaths.Count}");
            foreach (var p in fwdPaths.Take(3))
                Console.WriteLine($"     [{p.Length}] {p.Summary}");
        }

        // ② Entity → Repo 单跳
        var entityNodes = graphQuery.FindEntityNodesByTable("EQA_EquipGRelation");
        if (entityNodes.Count > 0)
        {
            Console.WriteLine($"② Entity→Repo [EQA_EquipGRelation]");
            var repoPaths = graphQuery.FindRepositoriesByTable("EQA_EquipGRelation");
            Console.WriteLine($"   路径: {repoPaths.Count}");
            foreach (var p in repoPaths)
            {
                var repoNode = graphQuery.GetNode(p.LeafId);
                Console.WriteLine($"     {p.Summary}");
                Console.WriteLine($"     → Repository: {repoNode?.Label}");
            }
        }

        // ③ 多类型边遍历 (call + nh:entity-access)
        var repoEntityPaths = SemanticTraversalEngine.Traverse(
            graphBuild.Index,
            entityNodes,
            new SemanticTraversalOptions
            {
                EdgeKinds = new HashSet<string>(StringComparer.Ordinal) { "nh:entity-access" },
                Direction = TraversalDirection.Backward,
                MaxDepth = 2,
                MaxPaths = 5
            });

        Console.WriteLine($"③ Multi-edge 遍历 (entity→repo→??)");
        Console.WriteLine($"   路径: {repoEntityPaths.Count}");
        foreach (var p in repoEntityPaths)
        {
            Console.WriteLine($"     [{p.Length}] {p.Summary}");
            Console.WriteLine($"     Edges: {string.Join(" → ", p.EdgeKinds)}");
        }

        Console.WriteLine("════════════════════════════════════");

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

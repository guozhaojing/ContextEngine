// =============================================================================
// Program.cs — 程序入口（交互式控制台）
// =============================================================================
// 流程：输入路径 → 扫描源码 → 导出 scan.json → 建图+分析 → 导出 graph.json → 示例查询
// =============================================================================

using Core.Export;
using Core.Export.Dtos;
using Core.Graph;
using Core.Graph.Analysis;
using Core.Graph.Analysis.Analyzers;
using Core.Graph.Analysis.GenericResolution;
using Core.Graph.Query;
using Core.Graph.Indexing;
using Core.Retrieval.Chunking;
using Core.Retrieval.Embedding;
using Core.Retrieval.VectorStore;
using Core.Retrieval.Retrieval;
using Core.Retrieval.Evaluation;
using Core.Retrieval.Explainability;
using Core.Context;
using Core.Scanning;
using System.Diagnostics;
using System.Text;

// UTF-8 输出保证中文提示正确显示；输入保持系统默认编码以正确处理中文路径
try { Console.OutputEncoding = Encoding.UTF8; } catch { }

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

        // ── Export Demo (Query 2.0 → Visualization) ─────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Graph Export 导出演示 ═══");

        var exportService = new GraphExportService(graphQuery);

        // 收集所有已发现的表名 (从 entity 节点提取)
        var tableNames = graphBuild.Graph.Nodes
            .Where(n => n.Kind == GraphNodeKind.Entity || n.Id.Contains("nh:entity", StringComparison.Ordinal))
            .Select(n =>
            {
                var lastSep = n.Id.LastIndexOf("::");
                return lastSep >= 0 ? n.Id[(lastSep + 2)..] : null;
            })
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();

        Console.WriteLine($"发现 {tableNames.Count} 张表");

        var allPaths = new List<SemanticPath>();
        var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "export");

        foreach (var tableName in tableNames)
        {
            Console.WriteLine($"  分析 [{tableName}]...");

            var impact = graphQuery.FindRoutesToTable(tableName);
            var apiToDb = graphQuery.FindTableImpact(tableName);
            var entityCenter = graphQuery.FindApisByEntity(tableName);

            allPaths.AddRange(impact);
            allPaths.AddRange(apiToDb);
            allPaths.AddRange(entityCenter);

            Console.WriteLine($"    Route→Table: {impact.Count} 条");
            Console.WriteLine($"    Table→Route: {apiToDb.Count} 条");
            Console.WriteLine($"    Entity Center: {entityCenter.Count} 条");
        }

        if (allPaths.Count > 0)
        {
            await exportService.SaveAllAsync(allPaths, exportDir, "ContextEngine Demo");

            Console.WriteLine();
            Console.WriteLine($"导出完成 → {Path.GetFullPath(exportDir)}");
            Console.WriteLine($"  nodes.json         ({exportService.ExportNodes(allPaths, ProjectionMode.Visualization).NodeCount} 节点)");
            Console.WriteLine($"  edges.json         ({exportService.ExportEdges(allPaths, ProjectionMode.Visualization).EdgeCount} 边)");
            Console.WriteLine($"  paths.json         ({allPaths.Count} 路径)");
            Console.WriteLine($"  visualization.json  (视图 + Layout 配置)");
        }
        else
        {
            Console.WriteLine("未找到可导出的路径 (需要 NHibernate 分析结果)");
        }

        // ── Chunk Export (Retrieval Engine) ────────────────────────
        var chunkService = new ChunkExportService(graphQuery);
        var chunkPath = await chunkService.SaveAsync(exportDir);

        var chunkExport = chunkService.ExportAll();
        Console.WriteLine();
        Console.WriteLine($"Chunk 导出 → {chunkPath}");
        Console.WriteLine($"  总计: {chunkExport.ChunkCount} chunks");
        foreach (var g in chunkExport.Chunks.GroupBy(c => c.Kind).OrderBy(g => g.Key))
            Console.WriteLine($"  [{g.Key}]: {g.Count()}");

        var topChunks = chunkExport.Chunks.OrderByDescending(c => c.ImportanceScore).Take(5);
        Console.WriteLine();
        Console.WriteLine("  Top 5 by importance:");
        foreach (var c in topChunks)
            Console.WriteLine($"    [{c.ImportanceScore:F1}] [{c.Kind}] {c.Title}");

        // ── Embedding + Hybrid Retrieval Demo ────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Embedding + Hybrid Retrieval 演示 ═══");

        var sw = Stopwatch.StartNew();

        // 1. Embedding pipeline
        var fakeProvider = new FakeEmbeddingProvider();
        var embedService = new EmbeddingExportService(fakeProvider);
        var embeddings = await embedService.GenerateAsync(chunkExport.Chunks);
        var embedPath = await embedService.SaveEmbeddingsAsync(embeddings, exportDir);

        Console.WriteLine($"Embedding 完成 → {embedPath}");
        Console.WriteLine($"  Model: {fakeProvider.ModelName} ({fakeProvider.Dimensions}d)");
        Console.WriteLine($"  Vectors: {embeddings.Count} (cached: {embeddings.Count(e => e.CreatedAt != DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))})");
        sw.Stop();
        Console.WriteLine($"  耗时: {sw.Elapsed.TotalMilliseconds:F0}ms");

        // 2. Vector store index
        var vectorStore = new InMemoryVectorStore();
        vectorStore.Index(embeddings);
        Console.WriteLine($"Vector Store: {vectorStore.Count} indexed");

        // 3. Build chunk index
        var chunkIndex = chunkExport.Chunks.ToDictionary(c => c.ChunkId, StringComparer.Ordinal);

        // 4. Hybrid retrieval engine
        var retrievalEngine = new HybridRetrievalEngine(vectorStore, fakeProvider, chunkIndex);

        // 5. Demo queries — 基于实际扫描项目的语义查询
        Console.WriteLine();
        Console.WriteLine("─── 检索示例 ───");

        await RunDemoQuery(retrievalEngine, "EQA_EquipGRelation 被哪些 API 访问", topK: 5);
        await RunDemoQuery(retrievalEngine, "质控数据 QCData 的读写流程", topK: 5,
            preferredTables: new[] { "EQA_EquipGRelation" });
        await RunDemoQuery(retrievalEngine, "质控图表 QCChart 绘制", topK: 5);

        // ── Benchmark + Explainability Demo ─────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Retrieval Benchmark + Explainability 演示 ═══");

        // Build benchmark cases from existing chunk data
        var benchmark = BuildDemoBenchmark(chunkExport.Chunks);

        var runner = new BenchmarkRunner(retrievalEngine);
        var benchmarkResult = await runner.RunAsync(benchmark);

        if (benchmarkResult.Aggregate is not null)
        {
            var agg = benchmarkResult.Aggregate;
            Console.WriteLine($"Benchmark: {benchmarkResult.Name} ({benchmarkResult.CaseCount} cases)");
            Console.WriteLine($"  Avg Recall@{benchmarkResult.Cases[0].TopK}:  {agg.AvgRecall:P1}");
            Console.WriteLine($"  Avg Precision@{benchmarkResult.Cases[0].TopK}: {agg.AvgPrecision:P1}");
            Console.WriteLine($"  Avg MRR:            {agg.AvgMRR:F3}");
            Console.WriteLine($"  Avg NDCG:           {agg.AvgNDCG:F3}");
            Console.WriteLine($"  Hit Rate:           {agg.HitRate:P1}");
            Console.WriteLine($"  Layer Coverage:     {agg.AvgLayerCoverage:P1}");
            Console.WriteLine($"  Entity Coverage:    {agg.AvgEntityCoverage:P1}");
            Console.WriteLine($"  Route Coverage:     {agg.AvgRouteCoverage:P1}");
            Console.WriteLine($"  Total Failures:     {agg.TotalFailures}");
            Console.WriteLine($"  Avg Search Time:    {agg.AvgSearchTimeMs:F0}ms");
        }

        // Explainability demo: explain top result
        Console.WriteLine();
        Console.WriteLine("─── Explainability ───");
        var explainQuery = new RetrievalQuery { Query = "EQA_EquipGRelation 数据访问", TopK = 3 };
        var explainResult = await retrievalEngine.SearchAsync(explainQuery);
        var explanations = RetrievalExplainer.ExplainAll(explainResult, explainQuery, 3);

        foreach (var ex in explanations)
        {
            Console.WriteLine($"  [{ex.Scores.FusedScore:F3}] {ex.ChunkTitle}");
            Console.WriteLine($"    breakdown: vec={ex.Scores.VectorSimilarity:F3} graph={ex.Scores.GraphRelevance:F3} biz={ex.Scores.BusinessRelevance:F3} imp={ex.Scores.ImportanceScore:F1}");
            Console.WriteLine($"    summary:  {ex.Summary}");
            if (ex.MatchedKeywords.Count > 0)
                Console.WriteLine($"    keywords: {string.Join(", ", ex.MatchedKeywords.Take(10))}");
            if (ex.SharedTables.Count > 0)
                Console.WriteLine($"    tables:   {string.Join(", ", ex.SharedTables)}");
            if (ex.SharedEntities.Count > 0)
                Console.WriteLine($"    entities: {string.Join(", ", ex.SharedEntities)}");
        }

        // Export benchmark
        var benchmarkExport = new BenchmarkExportService();
        var benchmarkPath = await benchmarkExport.SaveAsync(benchmarkResult, exportDir);
        Console.WriteLine();
        Console.WriteLine($"Benchmark 导出 → {benchmarkPath}");

        // ── Context Builder Demo ────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Context Assembly 演示 ═══");

        var contextBuilder = new ContextBuilder(graphQuery);
        var contextAssembler = new ContextAssembler(contextBuilder, maxTokens: 4096);

        var contextQuery = new RetrievalQuery { Query = "EQA_EquipGRelation 数据访问", TopK = 5 };
        var contextRetrieval = await retrievalEngine.SearchAsync(contextQuery);

        var contextDoc = contextAssembler.Assemble("eqa-equip-relation", contextRetrieval);
        Console.WriteLine($"  Document: {contextDoc.Id}");
        Console.WriteLine($"  Tokens: {contextDoc.BudgetUsed} / {contextDoc.BudgetMax}");
        Console.WriteLine($"  Sections: {contextDoc.Sections.Count}");
        foreach (var s in contextDoc.Sections.OrderByDescending(s => s.Priority))
            Console.WriteLine($"    [P{s.Priority}] {s.Title} ({s.TokenCount}t)");

        // Export
        var ctxExporter = new PromptContextExporter();
        var ctxMdPath = await ctxExporter.SaveMarkdownAsync(contextDoc, exportDir);
        var ctxJsonPath = await ctxExporter.SaveJsonAsync(contextDoc, exportDir);
        Console.WriteLine();
        Console.WriteLine($"  Context Markdown → {ctxMdPath}");
        Console.WriteLine($"  Context JSON     → {ctxJsonPath}");

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

static async Task RunDemoQuery(
    HybridRetrievalEngine engine,
    string queryText,
    int topK = 5,
    IReadOnlyList<string>? preferredTables = null)
{
    var query = new RetrievalQuery
    {
        Query = queryText,
        TopK = topK,
        PreferredTables = preferredTables,
        ExpandPaths = true
    };

    var result = await engine.SearchAsync(query);

    Console.WriteLine();
    Console.WriteLine($"  Query: \"{queryText}\"");
    Console.WriteLine($"  Results: {result.Candidates.Count} (from {result.TotalChunksSearched} chunks, {result.VectorCandidates} vector candidates, {result.SearchTimeMs:F0}ms)");

    for (var i = 0; i < result.Candidates.Count; i++)
    {
        var c = result.Candidates[i];
        var meta = c.Chunk.Metadata;
        Console.WriteLine($"    #{i + 1} [{c.FusedScore:F3}] [{c.Chunk.Kind}] {c.Chunk.Title}");
        Console.WriteLine($"          vec={c.VectorSimilarity:F3} graph={c.GraphRelevance:F3} biz={c.BusinessRelevance:F3} imp={c.Chunk.ImportanceScore:F1}" + 
            (meta is not null
                ? $" epDist={meta.EntryPointDistance} dataDist={meta.DataAccessDistance} fanIn={meta.FanIn} fanOut={meta.FanOut}"
                : ""));
    }
}

return 0;

static RetrievalBenchmark BuildDemoBenchmark(IReadOnlyList<CodeChunk> chunks)
{
    // Find chunks that match expected criteria
    var routeChunks = chunks.Where(c => c.Kind == ChunkKind.Route).ToList();
    var entityChunks = chunks.Where(c => c.Kind == ChunkKind.EntityAccess).ToList();
    var pathChunks = chunks.Where(c => c.Kind == ChunkKind.SemanticPath).ToList();

    var cases = new List<BenchmarkCase>();

    if (routeChunks.Count > 0)
    {
        var qcRoute = routeChunks.FirstOrDefault(c =>
            c.Title.Contains("QC", StringComparison.OrdinalIgnoreCase) ||
            c.Title.Contains("/api", StringComparison.OrdinalIgnoreCase));

        cases.Add(new BenchmarkCase
        {
            CaseId = "eqa-entity-access",
            Query = "EQA_EquipGRelation 实体数据访问",
            Expected = new BenchmarkExpected
            {
                ChunkIds = qcRoute is not null ? new[] { qcRoute.ChunkId } : [],
                EntityNames = new[] { "EQA_EquipGRelation" },
                TableNames = new[] { "EQA_EquipGRelation" },
                LayerNames = new[] { "Route", "Service", "Repository" }
            },
            TopK = 5,
            MinRecall = 0.2,
            MinMRR = 0.3
        });
    }

    if (entityChunks.Count > 0)
    {
        cases.Add(new BenchmarkCase
        {
            CaseId = "qc-data-flow",
            Query = "QCData 统计分析流程",
            Expected = new BenchmarkExpected
            {
                EntityNames = entityChunks.SelectMany(c => c.EntityNames).Distinct().Take(3).ToList(),
                TableNames = entityChunks.SelectMany(c => c.TableNames).Distinct().Take(3).ToList(),
                LayerNames = new[] { "Route", "Entity" }
            },
            TopK = 5,
            MinRecall = 0.1,
            MinMRR = 0.2
        });
    }

    if (pathChunks.Count > 0)
    {
        cases.Add(new BenchmarkCase
        {
            CaseId = "multi-layer-traversal",
            Query = "从 API 到数据库的完整调用链",
            Expected = new BenchmarkExpected
            {
                LayerNames = new[] { "Route", "Controller", "Service", "Repository", "Entity" }
            },
            TopK = 5,
            MinRecall = 0.1,
            MinMRR = 0.2
        });
    }

    return new RetrievalBenchmark
    {
        Name = "ContextEngine Retrieval Benchmark",
        Cases = cases
    };
}

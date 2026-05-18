# ContextEngine Architecture

## 分层

```
                    ┌────────────────────────┐
                    │     Program.cs          │  ← 入口 / 交互循环
                    └───────────┬────────────┘
                                │
        ┌───────────────────────┼───────────────────────┐
        ▼                       ▼                       ▼
┌──────────────┐  ┌──────────────────────┐  ┌──────────────────┐
│   Scanning   │  │  Graph.Analysis       │  │    Graph.Query    │
│   (Stage 1)  │  │  (Stage 3)           │  │    (Stage 5)      │
│              │  │                      │  │                   │
│ Discovery    │  │ Orchestrator          │  │ GraphQueryService │
│ SyntaxTree   │  │  ├ Pipeline           │  │  ├ GetCallers    │
│ SemanticModel│  │  ├ Analyzer[]         │  │  ├ GetCallees    │
│ CodeUnit     │  │  └ MergeService       │  │  ├ GetCallChain  │
└──────┬───────┘  │                      │  │  └ FindEntryPoints│
       │          └──────────┬───────────┘  └────────┬─────────┘
       │                     │                       │
       ▼                     ▼                       ▼
┌──────────────┐  ┌──────────────────────┐  ┌──────────────────┐
│  Semantics   │  │    Graph.Building     │  │  Graph.Indexing   │
│  (Stage 2)   │  │    (Stage 4)          │  │                   │
│              │  │                      │  │ GraphAdjacency... │
│ Resolution   │  │ CodeGraphBuilder     │  │ GraphIndex        │
│ ResolvedInfo │  │  ├ MethodRegistry    │  │                   │
│              │  │  ├ TargetResolver    │  │ CalledBy 物化      │
└──────────────┘  │  └ ExternalNodes    │  │ Callers/Callees   │
                  └──────────┬───────────┘  │                   │
                             │              └──────────────────┘
                             ▼
                  ┌──────────────────────┐
                  │   Graph.Identity      │
                  │                      │
                  │ MethodIdBuilder      │
                  │ MethodId              │
                  └──────────────────────┘
```

## 五阶段数据流

```
Input Path
 │
 │  Stage 1: Scanning
 ▼
SolutionScanResult
 ├── ScanRoot
 ├── Projects[]          (每个 .csproj 一个 ProjectScanGroup)
 └── AllCodeUnits[]      (扁平化 CodeUnit 集合)
 │
 │  Stage 2: Semantics
 ▼
CodeUnit (每个方法)
 ├── Id                  (MethodId)
 ├── Namespace / ClassName / MethodName
 ├── ParameterTypes      (区分重载)
 ├── Content             (方法体源码)
 └── ResolvedCalls[]     (Roslyn GetSymbolInfo 结果)
 │
 │  Stage 3: Building
 ▼
CodeGraph
 ├── ScanRoot
 ├── Nodes[]             (GraphNode)
 ├── Edges[]             (GraphEdge)
 └── Facts[]             (GraphFact, 初始为空)
 │
 │  Stage 4: Analysis
 ▼
GraphAnalysisPipeline.Run(scan, baseGraph, scope)
  ├── Analyzer1.Analyze(ctx) → GraphAnalysisResult
  ├── Analyzer2.Analyze(ctx) → GraphAnalysisResult
  └── ...
      │
      ▼
GraphAnalysisMergeService.Merge(baseGraph, results, scope)
  ├── Clone
  ├── Remove (增量)
  ├── Apply Facts + Annotations + ExtraEdges
  └── Rebuild GraphIndex
 │
 │  Stage 5: Query
 ▼
GraphQueryService (只读)
 ├── GetCallers(id)      → IReadOnlyList<string>
 ├── GetCallees(id)      → IReadOnlyList<string>
 ├── GetCallChain(id, depth) → 路径列表
 └── FindEntryPoints(id) → 无上游节点的入口方法集
```

## 生命周期

```
启动
  扫描器实例化           → ProjectCodeScanner
  分析器注册             → Orchestrator(new IGraphAnalyzer[] { ... })
  ─────────────────────────────────

每次扫描 (交互循环)
  1. scanner.ScanAsync(path)
        发现项目 → 枚举 .cs → 解析语法树 → 语义解析 → CodeUnit[]
        │
        ▼ 产出 SolutionScanResult
  2. CodeUnitJsonExporter.SaveAsync(scan)     → scan-*.json
        │
        ▼
  3. orchestrator.BuildAndAnalyze(scan)
       ├ Builder.Build(scan)                 → CodeGraph (基础调用图)
       ├ Pipeline.Run(scan, graph, scope)
       │    └ 各 Analyzer.Analyze(ctx)       → GraphAnalysisResult
       └ MergeService.Merge(graph, results)  → CodeGraphBuildResult
        │
        ▼ 产出 CodeGraphBuildResult { Graph, Index }
  4. CodeGraphJsonExporter.SaveAsync(graph)  → graph-*.json
        │
        ▼
  5. query = new GraphQueryService(buildResult)
       └ 内存查询 (不修改图, 仅读 Index)
        │
        ▼ 输出统计、方法摘要、示例查询

  6. 循环 → 等待下一个路径
```

## 核心不变式

| 约束 | 说明 |
|------|------|
| Builder 只读 Scan，只写本体 Graph | 不接触 Analysis 层 |
| Analyzer 只读 Snapshot，只写 Result | 不接触 Graph 本体 |
| Merge 是唯一写图入口 | 所有分析结果经 Merge 入图 |
| Query 只读 Index | 不接触 Builder / Analyzer / Merge |
| MethodId 稳定 | 源文件移动不影响，便于增量 diff |
| GraphIndex 只读 | 查询不建图，建图不查询 |

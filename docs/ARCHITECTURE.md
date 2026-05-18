# ContextEngine Architecture

## Pipeline

```
User Input (path)
    │
    ▼
┌──────────────────────────────────────────────────────────────┐
│ 1. SCANNING                                                  │
│    SolutionProjectDiscovery → DiscoveredProject[]             │
│    ProjectCodeScanner → SolutionScanResult                   │
│    Output: scan-YYYYMMDD-HHmmss.json                         │
└──────────────┬───────────────────────────────────────────────┘
               │ CodeUnit[] (2,547 units across 29 projects)
               ▼
┌──────────────────────────────────────────────────────────────┐
│ 2. GRAPH BUILDING                                            │
│    CodeGraphBuilder.Build(scan)                              │
│    → MethodRegistry → SemanticCallTargetResolver             │
│    → GraphNode/GraphEdge creation                            │
│    → GraphAdjacencyMaterializer (CalledBy)                   │
│    → GraphIndex.Build (Callers/Callees/EdgeIndex)            │
│    Output: CodeGraphBuildResult { Graph, Index }             │
└──────────────┬───────────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────────┐
│ 3. ANALYSIS (4 analyzers, sequential)                        │
│    AspNetRouteAnalyzer   → http-route facts, entry-points    │
│    SpringBeanAnalyzer    → spring:implements edges           │
│    NHibernateAnalyzer    → nh:entity-access edges/facts      │
│    NhSessionGenericAnalyzer → generic entity resolution      │
│    GraphAnalysisMergeService → merged into CodeGraph         │
│    GraphIndex rebuilt                                        │
│    Output: graph-YYYYMMDD-HHmmss.json                        │
└──────────────┬───────────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────────┐
│ 4. QUERY                                                     │
│    GraphQueryService (read-only, depends only on GraphIndex) │
│    - Callers/Callees/CallChain/EntryPoints                   │
│    - Semantic: FindRoutesToTable/FindTableImpact/...         │
│    SemanticTraversalEngine: multi-hop BFS with edge filters  │
└──────────────┬───────────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────────┐
│ 5. QUERY UNDERSTANDING (Phase 5B)                            │
│    VocabularyBuilder → ProjectVocabulary                     │
│    QueryAnalyzer → Intent + Expansion + Rewrite + Explain    │
│    Output: vocabulary.json, rewritten-query trace            │
└──────────────────────────────────────────────────────────────┘
```

## Input/Output

| Stage | Input | Output |
|-------|-------|--------|
| Scan | .sln / .csproj / directory | `scan-*.json` (CodeUnit[]) |
| Build | SolutionScanResult | CodeGraphBuildResult { Graph, Index } |
| Analyze | CodeGraph + GraphAnalysisContext | Facts, Annotations, ExtraEdges → merged into CodeGraph |
| Export | CodeGraph | `graph-*.json` |
| Query | GraphIndex | SemanticPath[] |
| QueryUnderstanding | ProjectVocabulary + AliasGraph | vocabulary.json, rewrite trace |

## Core Principles

1. **Stable MethodId**: `method:{normalizedProjectPath}::{Namespace.Class.Method(params)}` — no file paths, no line numbers
2. **Read-only query layer**: GraphQueryService depends only on GraphIndex; never modifies the graph
3. **Analyzer isolation**: IGraphAnalyzer implementations read from IGraphSnapshot, write via context (AddFact/AddAnnotation/AddExtraEdge); never modify GraphNode directly
4. **Merge is the sole writer**: GraphAnalysisMergeService clones, cleans, applies, and rebuilds
5. **Deterministic**: All analyzers use static analysis only; no LLM, no runtime reflection
6. **Explainable**: Every fact/edge/annotation traces back to source file + line; every query rewrite includes source and confidence

## Layer Dependency Direction

```
Scanning ──→ Models ──→ Semantics
                │
                ▼
          Graph.Building ──→ Graph (CodeGraph/GraphNode/GraphEdge)
                │
                ▼
          Graph.Identity (MethodId, MethodIdBuilder)
                │
                ▼
          Graph.Indexing (GraphIndex, EdgeIndex)
                │
                ▼
          Graph.Query (SemanticTraversalEngine, GraphQueryService)
                │
                ▼
          Graph.Analysis (IGraphAnalyzer, Pipeline, Merge)
                │
          ┌─────┴─────┐
          │            │
    Analyzers/    GenericResolution/
    (3 + 1)       (8 files, Phase 5C)
          │
          ▼
    Export (JSON serialization)
          │
          ▼
    QueryUnderstanding (Phase 5B, 10 files)
```

## Project Structure

```
happy-comet/
├── Program.cs                    # Interactive console entry point
├── ContextEngine.csproj          # net8.0, Roslyn 5.3.0, MSBuild Workspaces
├── Core/
│   ├── Models/                   # CodeUnit (1 file)
│   ├── Scanning/                 # ProjectCodeScanner, SolutionScanResult (4 files)
│   ├── Semantics/                # InvocationSemanticResolver, ResolvedMethodInfo (5 files)
│   ├── Graph/
│   │   ├── Identity/             # MethodId, MethodIdBuilder (2 files)
│   │   ├── Building/             # MethodRegistry, SemanticCallTargetResolver (2 files)
│   │   ├── Indexing/             # GraphIndex, EdgeIndex, EdgeInfo, GraphAdjacencyMaterializer (4 files)
│   │   ├── Traversal/            # GraphTraversal — cycle detection (1 file)
│   │   ├── Query/                # SemanticTraversalEngine, SemanticPath, SemanticTraversalOptions (3 files)
│   │   ├── Analysis/
│   │   │   ├── Analyzers/        # AspNetRouteAnalyzer, NHibernateAnalyzer, SpringBeanAnalyzer (3 files)
│   │   │   ├── GenericResolution/ # Phase 5C: NhSessionGenericAnalyzer + 7 support files (8 files)
│   │   │   └── (pipeline, merge, context, scope, facts, etc.) (12 files)
│   │   └── (CodeGraph, GraphNode, GraphEdge, builder, query service) (7 files)
│   ├── QueryUnderstanding/       # Phase 5B: 10 files
│   └── Export/                   # JSON exporters (3 files)
└── docs/                         # Architecture documentation (this directory)
```

## Invariants

| # | Rule |
|---|------|
| 1 | Builder reads Scan only; never reads Analysis output |
| 2 | Analyzer reads Snapshot only; writes via context |
| 3 | Merge is the sole writer to the merged graph |
| 4 | Query reads Index only; never traverses CodeGraph directly |
| 5 | MethodId is stable across scans; independent of source file path |
| 6 | GraphIndex is read-only once built |

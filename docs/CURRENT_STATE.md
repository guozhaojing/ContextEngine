# Current State

## Statistics (from last real scan)

| Metric | Value |
|--------|-------|
| Projects discovered | 29 |
| CodeUnits extracted | 2,547 |
| Graph nodes (internal) | ~2,547 |
| Graph nodes (external) | variable |
| Graph edges (total) | 14,230 |
| Resolved edges | variable |
| Facts produced | variable across 4 analyzers |
| **Tables discovered** | **5** (pre-Phase 5C) |
| **Route→Table paths** | **12** (pre-Phase 5C) |

## Completed Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Scanning — Roslyn-based C# solution/project discovery, SyntaxTree + SemanticModel extraction, CodeUnit generation | ✅ |
| 2 | Graph Building — Call graph construction, MethodId identity system, Callers/Callees adjacency materialization, GraphIndex | ✅ |
| 3 | Analysis Pipeline — IGraphAnalyzer interface, pipeline runner, merge service, incremental scope support | ✅ |
| 4 | Analyzers — AspNetRouteAnalyzer (http-route facts, entry-point annotations), SpringBeanAnalyzer (spring:implements edges), NHibernateAnalyzer (nh:entity-access edges, HQL extraction, .hbm.xml parsing) | ✅ |
| 5A | Semantic Traversal — BFS multi-hop engine with EdgeKind/NodeKind/MinConfidence filters, SemanticPath output, GraphQueryService semantic APIs | ✅ |
| 5B | Query Understanding — 10-file module: intent classification (6 types), vocabulary building from graph artifacts, PascalCase/snake_case normalization (23 abbreviation expansions + 18 CN↔EN pairs), query expansion (5 strategies), retrieval query rewriting, explainability output | ✅ |
| 5C | Generic Resolution — 8-file module: GenericInheritanceMap, GenericTypeResolver, GenericInvocationResolver, RepositoryPatternDetector, NhSessionGenericAnalyzer. Resolves entity access through generic BaseRepository\<T\>, IRepository\<T\>, DAO patterns. 4-level confidence (Exact→High→Medium→Low). Produces same nh:entity-access edges for seamless SemanticTraversal integration | ✅ |
| Meta-1 | Architecture Documentation — 11 docs consolidating entire system knowledge | ✅ |

## Uncompleted Phases (from original design)

| Phase | Description |
|-------|-------------|
| 4+ | Additional analyzers: EF Core SQL analyzer, MediatR handler analyzer, Dapper query analyzer |
| 5+ | Retrieval pipeline: chunked embedding, hybrid retrieval (dense + sparse), ranker implementation |
| 6 | Viewer: React Flow visualization, layer layout, path highlighting, filtering |
| 7 | Benchmarking: structured evaluation suite with precision/recall metrics |
| 8 | Incremental scanning: file-system watcher, partial graph rebuilds |
| 9 | IDE integration: Language Server Protocol, editor extensions |

## Current Breakthrough

**Generic Repository Resolution (Phase 5C)** — the system now resolves entity access through generic type layers:

- `class ReagentRepo : BaseRepository<EQA_Reagent>` → T=EQA_Reagent, links to Table=EQA_Reagents
- `IRepository<T>` interface implementations traced to concrete entity types
- `session.Query<T>()`, `repo.GetById()`, `_dao.Find()` detected through field/variable type analysis
- Same `nh:entity-access` edge kind reused → automatic SemanticTraversal integration
- Expected outcome: significant increase in discovered tables and Route→Table paths

## Current Blockers

1. **No real project test data**: Hardcoded `EQA_EquipGRelation` in Program.cs for demo. Need to test against real enterprise C# projects with NHibernate + generics.
2. **Static-only limitations**: Cannot resolve entities from runtime DI containers, reflection-based registrations, or dynamic HQL assembly.
3. **Interface ambiguity**: When multiple implementations of `IRepository<T>` exist, the analyzer may produce duplicate/fuzzy entity bindings.
4. **No embedding/retrieval pipeline**: Chunked embedding, vector store, and hybrid retrieval are designed but not implemented.
5. **No viewer/visualization**: SemanticPath output is text-only; no graph visualization layer.

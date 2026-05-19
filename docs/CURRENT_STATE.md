# Current State

## Statistics (from last real scan)

| Metric | Value |
|--------|-------|
| Projects discovered | 29 |
| CodeUnits extracted | 2,549 |
| Graph nodes (total) | 4,434 |
| Graph nodes (method) | 2,549 |
| Graph nodes (external/entity) | 1,885 |
| Graph edges (total) | 17,816 |
| Call edges | 14,218 |
| nh:entity-access edges | 3,598 |
| Facts produced | 4,945 |
| http-route facts | 927 |
| nh-entity-access facts | 3,997 |
| spring-bean facts | 21 |
| **Entities discovered** | **113** |
| **Exported paths** | **510** (1-hop: 364, 2-hop: 98, 3-hop: 37, 4-hop: 11) |

## Completed Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Scanning — Roslyn-based C# solution/project discovery, SyntaxTree + SemanticModel extraction, CodeUnit generation | ✅ |
| 2 | Graph Building — Call graph construction, MethodId identity system, Callers/Callees adjacency materialization, GraphIndex | ✅ |
| 3 | Analysis Pipeline — IGraphAnalyzer interface, pipeline runner, merge service, incremental scope support | ✅ |
| 4 | Analyzers — AspNetRouteAnalyzer (http-route facts, entry-point annotations), SpringBeanAnalyzer (spring:implements edges), NHibernateAnalyzer (nh:entity-access edges, HQL extraction, .hbm.xml parsing) | ✅ |
| 5A | Semantic Traversal — BFS multi-hop engine with EdgeKind/NodeKind/MinConfidence filters, SemanticPath output, GraphQueryService semantic APIs | ✅ |
| 5B | Query Understanding — 10-file module: intent classification (6 types), vocabulary building from graph artifacts, PascalCase/snake_case normalization (23 abbreviation expansions + 18 CN↔EN pairs), query expansion (5 strategies), retrieval query rewriting, explainability output | ✅ |
| 5C | Generic Resolution — 12-file module: GenericInheritanceMap (**Roslyn SyntaxTree**, 已替代 regex), GenericTypeResolver, GenericInvocationResolver (**Roslyn InvocationExpressionSyntax**), DaoFieldDetector (**Roslyn FieldDeclarationSyntax**), DaoCallSiteResolver (**Roslyn MemberAccessExpressionSyntax**), RepositoryPatternDetector, EntityClassRegistry, NhSessionGenericAnalyzer. Resolves entity access through generic BaseRepository\<T\>, IRepository\<T\>, DAO/BLL patterns. 5-level confidence (Exact→High→Medium→Low→None). Produces nh:entity-access edges for seamless SemanticTraversal integration. **Roslyn Refactoring Complete**: 零 regex 依赖，正确处理 partial class, nested class, file-scoped namespace, attributes, multiline declarations. | ✅ |
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

**Roslyn SyntaxTree Migration** — 整个 GenericResolution 子系统已从 regex 文本解析全面迁移到 Roslyn SyntaxTree/SemanticModel：

- `GenericInheritanceMap`: `ClassDeclarationSyntax` + `BaseListSyntax` + `GenericNameSyntax.TypeArgumentList`
- `GenericInvocationResolver`: `InvocationExpressionSyntax` + `GenericNameSyntax` + `LocalDeclarationStatementSyntax`
- `DaoFieldDetector`: `FieldDeclarationSyntax` / `PropertyDeclarationSyntax`
- `DaoCallSiteResolver`: `MemberAccessExpressionSyntax` + `InvocationExpressionSyntax`
- 成果: Entity 数 113 稳定不变, 图规模小幅增长 (+106 nodes 来自 regex 漏掉的 partial/nested/attribute 声明)
- **零 regex 依赖** — GenericResolution 目录中 0 处 `System.Text.RegularExpressions.Regex` 调用

## Current Blockers

1. **No real project test data**: Hardcoded `EQA_EquipGRelation` in Program.cs for demo. Need to test against real enterprise C# projects with NHibernate + generics.
2. **Static-only limitations**: Cannot resolve entities from runtime DI containers, reflection-based registrations, or dynamic HQL assembly.
3. **Interface ambiguity**: When multiple implementations of `IRepository<T>` exist, the analyzer may produce duplicate/fuzzy entity bindings.
4. **No embedding/retrieval pipeline**: Chunked embedding, vector store, and hybrid retrieval are designed but not implemented.
5. **No viewer/visualization**: SemanticPath output is text-only; no graph visualization layer.

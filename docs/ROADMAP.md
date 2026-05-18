# Roadmap

## Completed

- [x] Phase 1: Roslyn scanning (solution/project discovery, SyntaxTree, SemanticModel, CodeUnit extraction)
- [x] Phase 2: Graph building (call edges, MethodId, adjacency, GraphIndex)
- [x] Phase 3: Analysis pipeline (IGraphAnalyzer, merge service, incremental scope)
- [x] Phase 4: Analyzers — AspNetRoute, SpringBean, NHibernate (3 analyzers)
- [x] Phase 5A: Semantic traversal (BFS engine, EdgeKind/Confidence filters, SemanticPath)
- [x] Phase 5B: Query understanding (vocabulary, intent, expansion, rewriting, explanation)
- [x] Phase 5C: Generic resolution (inheritance map, repository patterns, generic invocation, entity resolution)

## Next Priorities

### Short-term

1. **Real project validation**
   - Run against enterprise NHibernate C# codebase with real EQA_* entities
   - Verify GenericResolution produces significant table/entity discovery increase
   - Measure Route→Table path count improvement
   - Fix any statistical gaps between expected vs actual entity coverage

2. **Chunked embedding pipeline (Phase 5D)**
   - Implement method-level chunking from CodeUnit.Content
   - Integrate local embedding model
   - Build vector store (in-memory or lightweight DB)
   - Implement hybrid retrieval (dense + sparse + graph-aware)

3. **NHibernate mapping extensibility**
   - Support Fluent NHibernate (code-based mapping detection)
   - Support NHibernate Mapping-by-Code conventions
   - Support `[Table("name")]` attributes

### Medium-term

4. **Additional analyzers**
   - EF Core: `DbContext.Set<T>()`, LINQ query detection
   - Dapper: `connection.Query<T>()`, raw SQL extraction
   - MediatR: `IRequestHandler<TRequest, TResponse>`, pipeline behaviors

5. **Query Understanding enhancements**
   - Chinese dictionary-based word segmentation
   - Domain-adaptive synonym expansion from project vocabulary
   - Embedding-based semantic matching for query expansion

6. **Viewer implementation (Phase 6)**
   - React Flow canvas with Dagre layout
   - Layer-based graph visualization
   - SemanticPath highlight + animation
   - Interactive filtering (Kind, Confidence, Depth)

### Long-term

7. **Incremental scanning**
   - File system watcher for `.cs` changes
   - Partial graph rebuild (only affected files + downstream)
   - Scan result caching (avoid re-parsing unchanged files)

8. **Performance optimization**
   - Parallel analyzer execution
   - Multi-threaded syntax tree parsing
   - GraphIndex compression for large codebases

9. **IDE integration**
   - Language Server Protocol (LSP) implementation
   - VS Code extension with inline impact analysis
   - Rider/Visual Studio plugin

## Deferred Ideas

- **Runtime tracer**: Hybrid static + runtime agent that instruments NHibernate at runtime to capture actual SQL → entity mappings, feeding back into the static model
- **Cross-repository linking**: Connect multiple scanned repositories (microservice topology)
- **Change impact prediction**: Given a diff, predict which routes/APIs are affected before running
- **Query language**: DSL for expressing semantic queries over the graph (e.g., `route -> entity -> table where table = 'Orders'`)
- **Auto-generated integration tests**: From Route→Table paths, generate API integration test stubs

## Non-Goals

- **LLM integration**: The system is intentionally deterministic and rule-based. No AI/LLM components will be added to the core analysis pipeline.
- **Runtime profiling**: No CLR profiler, no performance tracing, no production monitoring.
- **Database reverse-engineering**: Table schema is inferred from .hbm.xml or naming conventions only. No direct database connection.
- **Security/vulnerability scanning**: Not a SAST tool. Call graph analysis is semantic, not security-oriented.
- **Code generation**: No scaffolding, no boilerplate generation from analysis results.
- **Multi-language support**: C# only. No Java, Python, TypeScript, or other language analysis.

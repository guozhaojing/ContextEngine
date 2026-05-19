# Agent Context

> **Purpose**: High-density summary for AI agents working on this project.
> **Update**: After each phase completion.
> **Max**: 250 lines.

## Project Identity

**ContextEngine** — Roslyn-based C# code graph analysis tool. Scans solutions, builds call graphs with NHibernate entity resolution, provides semantic traversal and query understanding.

- **Language**: C# (.NET 8.0)
- **Entry**: `Program.cs` (interactive console: path → scan → build+analyze → query)
- **Packages**: Roslyn 5.3.0, MSBuild Workspaces
- **Test data**: None in repo. Uses scanned external C# projects.

## Current Phase: 5C Complete → Meta-1 Complete

### Completed
- Scanning (Solution → CodeUnit)
- Graph building (call edges, MethodId identity, GraphIndex)
- Analysis pipeline (IGraphAnalyzer, merge, scope)
- 4 analyzers: AspNetRoute (http-route facts), SpringBean (spring edges), NHibernate (nh:entity-access edges, HQL), GenericResolution (base class generic resolution)
- Semantic Traversal (multi-hop BFS with Kind/Confidence filters)
- Query Understanding (vocabulary, intent, expansion, rewriting, explanation)
- Generic Repository Resolution (inheritance map, 4-level confidence)
- Architecture Documentation (11 docs)

## Architecture Constraints

### DO
- All analyzers implement `IGraphAnalyzer` (Name + Analyze)
- Write via `context.AddFact/AddAnnotation/AddExtraEdge`
- Verify method exists: `context.NodesById.ContainsKey(methodId)`
- Use `StringComparer.Ordinal` for all dictionaries
- Use `MethodIdBuilder.FromMethod(projectPath, ns, className, methodName, paramTypes)`
- Add visible section headers in Chinese/English `// ==== X ==== //`
- Edge Kinds: `"call"`, `"nh:entity-access"`, `"spring:implements"`, `"spring:property-ref"`
- Produce facts with FactType, sourceFile, Data dictionary
- Entity node ID: `ext::nh:entity::{NS}.{Class}::{Table}`
- File-scoped namespaces: `namespace Core.Graph.Analysis.X;`

### DO NOT
- Modify GraphNode directly from analyzer
- Depend on GraphQueryService from analyzer
- Use runtime reflection or LLM
- Call CodeGraphBuilder from analyzer
- Modify GraphIndex
- Add comments unless clarifying non-obvious logic
- Introduce nullable without `<Nullable>enable</Nullable>` awareness
- Use `var` where type is not obvious from context
- Produce edges for Low confidence (GenericResolutionConfidence)

## Key Code Locations

| What | Where |
|------|-------|
| Entry point | `Program.cs` (top-level statements) |
| Scan entry | `Core/Scanning/ProjectCodeScanner.cs` |
| Build entry | `Core/Graph/CodeGraphBuilder.cs` → `Build()` |
| Merge entry | `Core/Graph/Analysis/GraphAnalysisMergeService.cs` |
| Query APIs | `Core/Graph/GraphQueryService.cs` |
| Traversal engine | `Core/Graph/Query/SemanticTraversalEngine.cs` |
| Analyzer interface | `Core/Graph/Analysis/IGraphAnalyzer.cs` |
| Analyzer context | `Core/Graph/Analysis/GraphAnalysisContext.cs` |
| Existing analyzers | `Core/Graph/Analysis/Analyzers/` (3 files) |
| Generic resolution | `Core/Graph/Analysis/GenericResolution/` (8 files) |
| Query understanding | `Core/QueryUnderstanding/` (10 files) |
| MethodId builder | `Core/Graph/Identity/MethodIdBuilder.cs` |
| Graph data types | `Core/Graph/GraphNode.cs`, `GraphEdge.cs`, `CodeGraph.cs` |
| Edge/Node kind constants | `Core/Graph/GraphNodeKind.cs`, `Core/Graph/GraphEdge.cs` (GraphEdgeKinds, EdgeLayer) |
| Export | `Core/Export/CodeGraphJsonExporter.cs`, `CodeUnitJsonExporter.cs` |

## Analyzer Registration

```csharp
// In Program.cs:
var graphOrchestrator = new CodeGraphAnalysisOrchestrator(new IGraphAnalyzer[]
{
    new AspNetRouteAnalyzer(),
    new SpringBeanAnalyzer(),
    new NHibernateAnalyzer(),       // ORIGINAL: handles session.Query<T>(), HQL
    new NhSessionGenericAnalyzer()  // Phase 5C: resolves generic repo/DAO patterns
});
```

**Important**: NHibernateAnalyzer and NhSessionGenericAnalyzer both produce `nh:entity-access` edges. They complement each other — NHibernateAnalyzer handles direct session API, NhSessionGenericAnalyzer handles generic layers.

## FactType Registry

| FactType | Producer | SubjectKind |
|----------|----------|-------------|
| `nh-entity-access` | NHibernateAnalyzer, NhSessionGenericAnalyzer | method |
| `nh-hql` | NHibernateAnalyzer | method |
| `nh-sql` | NHibernateAnalyzer | method |
| `http-route` | AspNetRouteAnalyzer | method |
| `spring-bean` | SpringBeanAnalyzer | method |

## Annotation Keys (merged to GraphNode.Attributes)

| Key | Producer | Value |
|-----|----------|-------|
| `route` | AspNetRouteAnalyzer | e.g. `/api/reagent/flow` |
| `http-method` | AspNetRouteAnalyzer | GET/POST/PUT/DELETE/PATCH |
| `entry-point` | AspNetRouteAnalyzer | `"true"` |
| `entity` | NHibernateAnalyzer, NhSessionGenericAnalyzer | entity class name |
| `table` | NHibernateAnalyzer, NhSessionGenericAnalyzer | table name |
| `api` | NHibernateAnalyzer | NH session method name |
| `generic:resolved` | NhSessionGenericAnalyzer | entity class name |
| `spring-bean-id` | SpringBeanAnalyzer | bean XML id |
| `spring-bean-type` | SpringBeanAnalyzer | bean type string |

## Edge Kind Registry

| Kind | Producer | From | To |
|------|----------|------|-----|
| `call` | CodeGraphBuilder | method node | method/external node |
| `nh:entity-access` | NHibernateAnalyzer, NhSessionGenericAnalyzer | method node | ext::nh:entity::{NS}.{Class}::{Table} |
| `spring:implements` | SpringBeanAnalyzer | interface method | implementation method |
| `spring:property-ref` | SpringBeanAnalyzer | bean method | dependent bean method |

## Next Phase Goals

- Phase 5D: Chunked embedding pipeline + hybrid retrieval
- Phase 6: Viewer (React Flow visualization)
- Validate GenericResolution against real enterprise NHibernate project (EQA_* entities)
- Measure table coverage improvement (5 → expected 50+)
- Measure Route→Table path improvement (12 → expected 200+)

## Current Known Issues

1. Table count = 5 (far below expected) — needs generic resolution validation against real data
2. Route→Table paths = 12 (partial coverage)
3. No real project test fixtures in repository
4. NHibernateAnalyzer + NhSessionGenericAnalyzer may produce duplicate edges for same (method, entity) pair — deduplication relies on seenEdges HashSet
5. GenericInheritanceMap 已全面迁移到 Roslyn SyntaxTree 解析，不再使用 regex。正确处理 partial class、nested class、file-scoped namespace、attributes、multiline declarations。

## Build & Run

```bash
dotnet build         # 0 warnings, 0 errors expected
dotnet run           # Interactive: enter path → scan + analyze + query
                     # Output: scan-*.json, graph-*.json in working directory
```

## Metric Targets (post-Phase 5C validation)

| Metric | Before | Target |
|--------|--------|--------|
| Tables discovered | 5 | 50+ |
| Route→Table paths | 12 | 200+ |
| Entity coverage | ~5 entities | 30+ entities |
| Generic resolution rate | N/A | >60% of repository methods |
| Edge count (nh:entity-access) | ~15 | 500+ |

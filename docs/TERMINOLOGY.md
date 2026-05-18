# Terminology

## Core Data Units

| Term | Definition | Source |
|------|------------|--------|
| **CodeUnit** | A single method extracted from source code. Contains: Id, FilePath, Namespace, ClassName, MethodName, ParameterTypes, Content, Calls, ResolvedCalls. | `Core/Models/CodeUnit.cs` |
| **SolutionScanResult** | Output of the scanning phase. Contains ScanRoot + list of ProjectScanGroup (each with ProjectName, ProjectPath, CodeUnits). | `Core/Scanning/SolutionScanResult.cs` |
| **DiscoveredProject** | Record: (Name, ProjectFilePath, ProjectDirectory). Found by .sln parsing or .csproj walk. | `Core/Scanning/DiscoveredProject.cs` |
| **MethodId** | Stable string identifier: `method:{normalizedProjectPath}::{Namespace.Class.Method(params)}`. Used as graph node primary key. | `Core/Graph/Identity/MethodId.cs` |

## Graph Entities

| Term | Definition |
|------|------------|
| **GraphNode** | A vertex in the code graph. Has: Id, Kind, Label, ProjectName, Namespace, ClassName, MethodName, ParameterTypes, IsExternal, CalledBy, Attributes. |
| **GraphEdge** | A directed edge A→B. Has: FromId, ToId, Call, IsResolved, Kind, Attributes. |
| **GraphNodeKind.Method** | `"method"` — a node representing a source-code method. Produced by CodeGraphBuilder. |
| **GraphNodeKind.Entity** | `"entity"` — a virtual external node representing an NHibernate-mapped entity class. ID format: `ext::nh:entity::{NS}.{Class}::{Table}`. |
| **GraphNodeKind.Table** | `"table"` — reserved for future database table nodes. Not yet produced. |
| **GraphNodeKind.External** | `"external"` — a method from an external library or unresolved call target. |
| **CalledBy** | Reverse edge materialization: for edge A→B, B.CalledBy contains A. Populated by GraphAdjacencyMaterializer. |
| **FanIn** | Number of edges pointing TO a node (CalledBy count). Measures how many callers depend on this method. |
| **FanOut** | Number of edges pointing FROM a node (Callees count). Measures how many dependencies this method has. |
| **EntryPoint** | A method with no callers (CalledBy empty) or annotated with `aspnet-route:entry-point`. Root of a call chain. |
| **EntryPointDistance** | Number of hops from a node backwards to the nearest EntryPoint. Used as a ranking signal. |
| **ExternalNode** | A node where IsExternal=true. Created when an edge target is not in the solution's code (e.g., framework method, unresolved call). |

## Edge Types

| Term | Definition |
|------|------------|
| **call** | EdgeKind: `"call"`. Roslyn-resolved A→B method invocation edge. Produced by CodeGraphBuilder. Layer: Call. |
| **nh:entity-access** | EdgeKind: `"nh:entity-access"`. Edge from a method to an Entity node representing NHibernate data access. Produced by NHibernateAnalyzer and NhSessionGenericAnalyzer. Layer: Data. |
| **spring:implements** | EdgeKind: `"spring:implements"`. Edge from an interface method to its Spring.NET implementation method. Produced by SpringBeanAnalyzer. Layer: Framework. |
| **spring:property-ref** | EdgeKind: `"spring:property-ref"`. Edge from a Spring.NET bean to a dependent bean via `<property ref="">`. Layer: Framework. |
| **EdgeLayer.Call** | `"call"` — plain method invocation layer. |
| **EdgeLayer.Framework** | `"framework"` — DI container, AOP, middleware edges. |
| **EdgeLayer.Data** | `"data"` — ORM/DB access edges. |
| **EdgeLayer.Transaction** | `"transaction"` — (reserved) unit-of-work, transaction boundary edges. |

## Analysis Artifacts

| Term | Definition |
|------|------------|
| **GraphFact** | Structured analysis fact written by an IGraphAnalyzer. Contains: Analyzer, SubjectId, SubjectKind, FactType, SourceFile, Data (Dictionary). |
| **GraphAnnotation** | Key-value annotation merged into GraphNode.Attributes as `"analyzerName:key" = value`. |
| **GraphExtraEdge** | Edge produced by an analyzer. Converted to GraphEdge during merge. External ToId nodes auto-created. |
| **FactType** | Category string for facts: `"nh-entity-access"`, `"nh-hql"`, `"nh-sql"`, `"http-route"`, `"spring-bean"`. |
| **IGraphAnalyzer** | Interface: `string Name`, `void Analyze(GraphAnalysisContext)`. All analyzers implement this. |
| **GraphAnalysisContext** | Read-only snapshot + write APIs (AddFact/AddAnnotation/AddExtraEdge) provided to each analyzer. |
| **GraphAnalysisMergeService** | Merges analyzer outputs into CodeGraph: clones graph, cleans old contributions, applies facts/annotations/edges, rebuilds index. |

## Query Layer

| Term | Definition |
|------|------------|
| **SemanticPath** | Output of SemanticTraversalEngine: ordered sequence of NodeIds with EdgeKinds, HopLabels, Summary. |
| **SemanticTraversalEngine** | Recursive DFS engine that explores the graph with Kind/Confidence/Depth filters. |
| **SemanticTraversalOptions** | Configuration for traversal: EdgeKinds, NodeKinds, Direction, MinConfidence, MaxDepth, TargetAttributeKey, MaxPaths. |
| **GraphIndex** | Read-only adjacency index: Nodes, Callers, Callees, EdgeIndex. Built once, never modified. |
| **EdgeIndex** | Bidirectional edge index: OutgoingByKind, IncomingByKind. Each entry is EdgeInfo { ToId, Kind, Label, Attributes }. |
| **GraphQueryService** | Public read-only query API layer. Depends only on GraphIndex. Provides Callers/Callees/CallChain/FindEntryPoints + semantic APIs. |

## Query Understanding (Phase 5B)

| Term | Definition |
|------|------------|
| **QueryIntent** | Classification: Unknown / FlowAnalysis / ImpactAnalysis / EntityLookup / RouteLookup / ValidationLookup. Rule-based keyword scoring. |
| **ProjectVocabulary** | Extracted from graph artifacts: Entities, Tables, Routes, Classes, Methods, NormalizedTerms, AliasGraph, Synonyms. |
| **VocabularyEntry** | One vocabulary term: Original, Normalized, Tokens, Kind, ProjectName, Frequency. |
| **NormalizedTerm** | Tokenized + normalized word: Original, Normalized, Source, Components. |
| **AliasGraph** | Bidirectional graph linking Table ↔ Entity ↔ Route ↔ Repository ↔ Controller via naming equivalence edges. |
| **SynonymMap** | User-language ↔ project-term bidirectional mapping. 18 Chinese↔English business pairs + cross-entry shared-token synonyms. |
| **QueryExpansion** | 5 strategies: synonym, vocabulary, fuzzy, suffix template, alias graph. Plus compound multi-token expansion. |
| **ExpansionCandidate** | One candidate term: Term, Source, Score, Kind. |
| **RetrievalQueryRewriter** | Produces RewrittenQuery: ExpandedQuery string + token list + intent + source trace. |
| **QueryExplanation** | Retrieval explanation: matched entities, tables, routes, keywords, classes, methods. Each with confidence and source. |

## Generic Resolution (Phase 5C)

| Term | Definition |
|------|------------|
| **GenericInheritanceMap** | Maps class declarations → base types + generic parameter bindings. Built from parsing all .cs files. |
| **ClassInheritanceInfo** | One class: FullName, TypeParameters, BaseTypes, GenericParameterBindings. |
| **GenericBaseType** | A base type reference: Name, FullName, IsGeneric, TypeArguments. |
| **GenericTypeResolver** | Resolves generic type parameter T to concrete entity type through inheritance chains. |
| **GenericInvocationResolver** | Parses method bodies for `repo.Get()`, `session.Query<T>()` patterns + variable type analysis. |
| **RepositoryPatternDetector** | Detects Repo/DAO/Service naming patterns (33 pattern strings across 4 categories). |
| **EntityResolution** | Result: EntityClass, ResolutionType, Confidence, ViaClass. |
| **GenericResolutionConfidence** | 5-level: None(0) / Low(1) / Medium(2) / High(3) / Exact(4). Maps to standard ResolutionConfidence. |

## Confidence Levels

| Term | Definition |
|------|------------|
| **ResolutionConfidence** | Standard: Low(0), Medium(1), High(2), Exact(3). Used in edge/fact attributes. |
| **GenericResolutionConfidence** | Extended: None(0), Low(1), Medium(2), High(3), Exact(4). Converts to standard via .ToStandardConfidence(). |
| **Exact** | Explicit generic argument extracted from source code. Highest reliability. |
| **High** | Inheritance chain type parameter resolution or HQL literal match. |
| **Medium** | Repository pattern matching, field type inference, argument variable type. |
| **Low** | Method name heuristics, variable name hints. Lowest reliability. |

## Export Format

| Term | Definition |
|------|------------|
| **scan-*.json** | Scan export: SolutionScanResult serialized as JSON. Contains ScanRoot, GeneratedAt, Projects[]. |
| **graph-*.json** | Graph export: CodeGraph serialized as JSON. Contains Nodes[], Edges[], Facts[] with all attributes. |
| **vocabulary.json** | Vocabulary export: ProjectVocabulary serialized as JSON via QueryAnalyzer.ExportVocabularyJson(). |
| **rewrite trace** | JSON trace of query rewriting: original query, intent, token expansions with scores, rewritten query. |

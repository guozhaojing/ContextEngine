# Graph Model

## Node Kinds (`GraphNodeKind`)

| Kind | Value | Source | Description |
|------|-------|--------|-------------|
| `Method` | `"method"` | CodeGraphBuilder | Every CodeUnit becomes a Method node. Stable MethodId. |
| `Entity` | `"entity"` | NHibernateAnalyzer, NhSessionGenericAnalyzer | Virtual external node: `ext::nh:entity::{NS}.{Class}::{Table}` |
| `Table` | `"table"` | (reserved, not yet produced) | Planned for database table nodes |
| `External` | `"external"` | CodeGraphBuilder | External library methods. Created when edge ToId is not in nodeMap. |

## Edge Kinds

| Kind | Value | Layer | Producer | Description |
|------|-------|-------|----------|-------------|
| `call` | `"call"` | Call | CodeGraphBuilder | Roslyn-resolved method A→B calls |
| `nh:entity-access` | `"nh:entity-access"` | Data | NHibernateAnalyzer, NhSessionGenericAnalyzer | Method → Entity node via NHibernate Session API |
| `spring:implements` | `"spring:implements"` | Framework | SpringBeanAnalyzer | Interface method → implementation method |
| `spring:property-ref` | `"spring:property-ref"` | Framework | SpringBeanAnalyzer | Bean → dependent bean via `<property ref="">` |

## Edge Layer Enumeration (`EdgeLayer`)

| Layer | Value | Epistemic Status |
|-------|-------|------------------|
| `Call` | `"call"` | Implemented |
| `Framework` | `"framework"` | Partially implemented (spring edges) |
| `Data` | `"data"` | Partially implemented (nh:entity-access) |
| `Transaction` | `"transaction"` | Designed, not implemented |

## SemanticTraversal

### Engine: `SemanticTraversalEngine`

- **Algorithm**: Recursive DFS with cycle detection (`visiting` set + path containment check)
- **Direction**: Forward (outgoing), Backward (incoming), or Both
- **Filters**: EdgeKinds (set), NodeKinds (set), MinConfidence (enum)
- **Termination**: MaxDepth reached, TargetAttributeKey matched, or no more edges
- **Deduplication**: Path signature (`nodeId-hop1-hop2-...`) deduplication; MaxPaths cap (default 200)

### Options: `SemanticTraversalOptions`

| Property | Type | Default |
|----------|------|---------|
| EdgeKinds | `IReadOnlySet<string>?` | null (all) |
| NodeKinds | `IReadOnlySet<string>?` | null (all) |
| Direction | `TraversalDirection` | Forward |
| MinConfidence | `ResolutionConfidence?` | null (no filter) |
| MaxDepth | `int?` | null (unlimited) |
| TargetAttributeKey | `string?` | null |
| TargetAttributeValue | `string?` | null |
| MaxPaths | `int` | 200 |
| DeduplicatePaths | `bool` | true |

Pre-built presets:
- `SemanticTraversalOptions.RouteToTable(tableName)` — kinds: call+nh:entity-access, forward, depth 15
- `SemanticTraversalOptions.TableImpact(tableName)` — kinds: call+nh:entity-access, backward, depth 15, target: "aspnet-route:entry-point"

### Path: `SemanticPath`

| Member | Type | Description |
|--------|------|-------------|
| PathId | `string` | Fingerprint |
| NodeIds | `IReadOnlyList<string>` | Node ID sequence |
| EdgeKinds | `IReadOnlyList<string>` | Kind per hop |
| HopLabels | `IReadOnlyList<string>` | Label per hop |
| Summary | `string` | Human-readable: `shortName ─[kind]→ shortName` |
| Length | `int` | Number of hops |
| RootId | `string` | First node |
| LeafId | `string` | Last node |

## EdgeIndex

Two-direction adjacency:

| Index | Key | Value |
|-------|-----|-------|
| `OutgoingByKind` | nodeId | `IReadOnlyList<EdgeInfo>` (grouped by Kind) |
| `IncomingByKind` | nodeId | `IReadOnlyList<EdgeInfo>` (grouped by Kind) |

`EdgeInfo` is a readonly struct: `{ ToId, Kind, Label, IsResolved, Attributes }`. For incoming edges, `ToId` stores the source node (from which the edge originates).

## GraphIndex

The read-only query surface:

| Member | Type |
|--------|------|
| `Nodes` | `IReadOnlyDictionary<string, GraphNode>` |
| `Callers` | `IReadOnlyDictionary<string, IReadOnlyList<string>>` |
| `Callees` | `IReadOnlyDictionary<string, IReadOnlyList<string>>` |
| `EdgeIdx` | `EdgeIndex` |

## Confidence Model

### Standard (`ResolutionConfidence`)

| Level | Value | Used By |
|-------|-------|---------|
| `Low` | 0 | HQL variable fallback |
| `Medium` | 1 | NHibernate arg type inference |
| `High` | 2 | HQL literal extraction, HBM exact match |
| `Exact` | 3 | Explicit generic argument `Query<EQA_Reagent>()` |

### Generic (`GenericResolutionConfidence`)

| Level | Value | Strategy |
|-------|-------|----------|
| `None` | 0 | No resolution |
| `Low` | 1 | Method name heuristic (GetReagentList→Reagent) |
| `Medium` | 2 | Repository pattern matching, field type derivation |
| `High` | 3 | Inheritance chain type parameter resolution |
| `Exact` | 4 | Explicit generic argument |

Conversion: Exact→Exact, High→High, Medium→Medium, Low/None→Low.

## Facts (`GraphFact`)

| Field | Description |
|-------|-------------|
| Analyzer | Which analyzer produced this |
| SubjectId | Target method/entity ID |
| SubjectKind | `method` / `file` / `project` / `edge` |
| FactType | Category: `nh-entity-access`, `http-route`, `nh-hql`, `nh-sql`, `spring-bean` |
| SourceFile | Relative file path (for incremental cleanup) |
| Data | `Dictionary<string, string>` key-value payload |

## GraphQueryService Semantic APIs

| API | Description |
|-----|-------------|
| `FindRoutesToTable(tableName)` | Routes → Controllers → Services → Repos → Entity → Table |
| `FindTableImpact(tableName)` | Table → Entity → Repo → Service → Controller → Route |
| `FindApisByEntity(entityClass)` | Entity → backward to API entry points |
| `FindRepositoriesByTable(tableName)` | Entity → 1-hop backward via nh:entity-access edges |
| `FindImpactByMethod(methodId)` | Both directions: call + spring + nh edges, depth 8 |
| `FindSemanticPath(fromId, toId, options)` | General multi-hop |
| `FindEntityNodesByTable(tableName)` | Filter IDs: `ext::nh:entity` + `::tableName` |
| `FindEntityNodesByClass(entityClass)` | Filter IDs: `ext::nh:entity` + `.entityClass::` |
| `FindEntryPointNodes()` | Nodes with `aspnet-route:entry-point` attribute |

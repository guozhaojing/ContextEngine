# Generic Resolution (Phase 5C)

## Problem

Enterprise NHibernate projects hide data access behind generic patterns:

```
class ReagentRepo : BaseRepository<EQA_Reagent>     // Entity hidden in type param
IRepository<EQA_Reagent> → ReagentRepository        // Interface obscures entity
session.Query<T>()                                   // T resolved at call site only
_repo.GetById(id)                                    // Entity inferred from field type
```

Pre-Phase 5C, the NHibernateAnalyzer only detected direct `session.Query<EQA_Reagent>()` calls and HQL string literals — missing all generic-layer data access.

## Architecture

8 files under `Core/Graph/Analysis/GenericResolution/`:

```
GenericResolutionConfidence.cs    # 5-level confidence (None/Medium/High/Exact)
GenericEntityAccessFact.cs        # Fact DTO with ToFactData()/ToEdgeLabel()
GenericInheritanceMap.cs          # Scans class declarations, builds inheritance tree
GenericTypeResolver.cs            # Resolves T→concrete entity through inheritance
GenericInvocationResolver.cs      # Parses method bodies for generic invocations
RepositoryPatternDetector.cs      # Detects Repo/Dao/Service naming patterns
GenericResolutionResult.cs        # Result collector with statistics
NhSessionGenericAnalyzer.cs       # IGraphAnalyzer implementation (main entry)
```

## Core Data Flow

```
                     ┌─────────────────┐
                     │  CodeUnits[2547] │
                     └────────┬────────┘
                              │ Build()
                              ▼
                   ┌──────────────────────┐
                   │ GenericInheritanceMap │
                   │                       │
                   │ Classes: {            │
                   │   "BaseRepository<T>":│
                   │     TypeParams: [T]   │
                   │   "ReagentRepo":      │
                   │     BaseTypes: [      │
                   │       BaseRepository<│
                   │         EQA_Reagent>  │
                   │     ],                │
                   │   ...                 │
                   │ }                     │
                   └──────────┬───────────┘
                              │
          ┌───────────────────┼───────────────────┐
          ▼                   ▼                    ▼
  GenericTypeResolver   RepositoryPattern    GenericInvocationResolver
  "Resolve T in         Detector             "Resolve entity
   ReagentRepo"         "ReagentRepo         from _repo.Get()"
                         matches Repo         via field type
   T = EQA_Reagent       pattern"            analysis"

          └───────────────────┼───────────────────┘
                              ▼
                    ┌─────────────────────┐
                    │   EntityResolution   │
                    │   EntityClass:       │
                    │     "EQA_Reagent"    │
                    │   Confidence: Exact   │
                    │   ViaClass:           │
                    │     "ReagentRepo"    │
                    └──────────┬──────────┘
                              │ ProduceEntityAccess()
                              ▼
              ┌───────────────────────────────┐
              │  nh:entity-access Edge        │
              │  ReagentRepo.Get() ──→        │
              │  ext::nh:entity::EQA_Reagent  │
              │              ::EQA_Reagents   │
              │                               │
              │  confidence: exact            │
              │  generic:resolved: true       │
              │  viaClass: ReagentRepo        │
              └───────────────────────────────┘
```

## 4-Level Confidence Strategy

| Level | Score | Trigger | Example |
|-------|-------|---------|---------|
| `Exact` | 4 | Explicit generic argument in invocation | `session.Query<EQA_Reagent>()` |
| `High` | 3 | Inheritance chain type parameter resolution | `class Foo : BaseRepo<Bar>` → T=Bar |
| `Medium` | 2 | Repository pattern match + field type inference | `_repo.Get(id)` where `_repo` is `FooRepo` |
| `Low` | 1 | Method name heuristic | `GetReagentList()` → Reagent |

## GenericInheritanceMap

Parses all `.cs` files for class declarations:

- `class Foo<T> : BaseBar<Concrete>`
- Tracks: `FullName, Name, Namespace, TypeParameters, BaseTypes[], GenericParameterBindings{}`
- `ResolveTypeParameter(classInfo, "T")` → recursive through base types (max depth 10)
- `ResolveAllArguments(classInfo)` → for each base type's generic args, resolve to concrete type

## GenericInvocationResolver

Scans method bodies for invocation patterns using regex:

1. **Direct generic calls**: `receiver.Method<GenericArg>(...)`
2. **Session API**: receiver contains "session" → Exact
3. **Repository pattern**: receiver type resolved via field/variable analysis → Medium-High
4. **Class pattern**: method class matches Repository/DAO naming → High

Detected API methods (18 each for session, repository, generic categories).

Variable type analysis: parses field declarations (`private Repo<EQA_Reagent> _repo;`), var declarations, typed variable declarations, constructor parameter injection.

## RepositoryPatternDetector

Recognizes 4 pattern categories:

| Category | Examples (count) |
|----------|------------------|
| Suffix | Repository, Repo, Dao, DAO, Dal, DataAccess, DataProvider, QueryProvider (8) |
| Service suffix | Service, ServiceImpl, Manager, Handler, Provider, Processor, Controller (7) |
| Generic base | Repository, BaseRepository, GenericRepository, Dao, BaseDao, GenericDao, BaseService, GenericService, CrudRepository, PagingAndSortingRepository, JpaRepository, MongoRepository (12) |
| Interface | IRepository, IDao, IReadRepository, IWriteRepository, ICrudService, IEntityService (6) |

Entity name extraction: strip suffix from class name (e.g., `ReagentRepository` → `Reagent`), strip `Impl` from `ServiceImpl` (e.g., `ReagentServiceImpl` → `Reagent`).

## Integration

- **Edge Kind**: `"nh:entity-access"` — same as NHibernateAnalyzer, transparent to SemanticTraversalEngine
- **Annotations**: `"generic:resolved" = entityClass`, `"entity"`, `"table"`, `"api" = "generic"`
- **Facts**: FactType `"nh-entity-access"` with additional `"viaClass"`, `"resolution"`, `"generic:resolved"` fields
- **External nodes**: Format `ext::nh:entity::{NS}.{Class}::{Table}` — same as NHibernateAnalyzer
- **Confidence**: Only Medium+ generates ExtraEdges (prevents noise)

## Known Blind Spots

1. **Runtime DI**: Cannot resolve entities registered via `services.AddScoped<IRepository<T>, Repository<T>>()` — Spring.NET `<object>` XML only
2. **Reflection**: `typeof(T).Name`, `Activator.CreateInstance<T>()`, `MakeGenericMethod()` not analyzed
3. **Dynamic HQL**: String-assembled HQL (`"from " + entityType.Name`) not traced through generics
4. **Multi-level wrapping**: `Service<T>` → `Repository<T>` → `Session.Query<T>()` — T propagation limited to 10 depth
5. **Open generics**: `IRepository<>` without type parameter binding cannot be resolved

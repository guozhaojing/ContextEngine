# Known Limitations

## Static Analysis Bounds

All analyzers use **syntax-tree-level static analysis only**. They cannot resolve:

### Runtime DI Containers
```csharp
// NOT resolvable
services.AddScoped<IRepository<Order>, OrderRepository>();
builder.RegisterGeneric(typeof(Repository<>)).As(typeof(IRepository<>));
```

Only Spring.NET `<object>` XML registrations are detected (via SpringBeanAnalyzer).

### Reflection-Based Access
```csharp
// NOT resolvable
var entityType = typeof(T);
var method = typeof(Session).GetMethod("Query").MakeGenericMethod(entityType);
Activator.CreateInstance(entityType);
```

### Dynamic HQL Assembly
```csharp
// NOT resolvable (via generics)
var hql = $"from {typeof(T).Name} o where o.Status = :status";
session.CreateQuery(hql).SetParameter("status", status).List();
```

The NHibernateAnalyzer can trace HQL from string literals and nearby variable assignments within the same method, but cannot trace across generic layers where `typeof(T).Name` is used.

## Interface Ambiguity

When multiple classes implement `IRepository<T>`:
```csharp
class OrderRepo : IRepository<Order> { }
class CustomerRepo : IRepository<Customer> { }
```

The GenericInvocationResolver cannot determine which implementation a field typed as `IRepository<T>` refers to without runtime context.

## Partial Route-to-Table Coverage

**Root cause**: Entity access detection relies on NHibernate Session API calls. Methods that access data through:
- Stored procedure calls
- Raw ADO.NET
- Dapper
- Entity Framework
- File I/O

...will not produce `nh:entity-access` edges, breaking Route→Table chains.

## HBM XML Dependency

Entity↔Table mapping primarily comes from `.hbm.xml` files. If a project uses:
- Fluent NHibernate (code-based mappings)
- NHibernate Mapping-by-Code
- Attributes-based mapping

...table names fall back to simple `ClassName + "s"` pluralization, which may be incorrect.

## Method Identifier Sensitivity

`MethodId` is built from:
- **Project path** (normalized, lowercase)
- **Namespace.Class.Method(paramTypes)**

Changes that break MethodId stability:
- Renaming a .csproj file or moving it
- Changing a method's parameter types
- Renaming namespaces, classes, or methods

## Open Generic Types

`IRepository<>` (without type parameter) cannot be resolved:
```csharp
// T is unknown until runtime
public class GenericService<T> where T : class {
    private readonly IRepository<T> _repo;  // T not bound
}
```

## Nested Generic Propagation

Type parameter propagation limited to recursion depth 10:
```csharp
// Layer 1-2 resolved; deeper layers may fail
class A<T> : B<T> {}
class B<T> : C<T> {}
class C<T> : D<T> {} // etc.
```

## Performance Characteristics

| Operation | Characteristics |
|-----------|----------------|
| Scanning | O(files × methods). 29 projects, 2,547 CodeUnits → ~10-30s |
| Graph building | O(edges). 14,230 edges → <1s |
| NHibernateAnalyzer | O(files × invocations). Full syntax tree parse per file |
| NhSessionGenericAnalyzer | O(files × class_declarations × invocations). Roslyn SyntaxTree traversal per file |
| GenericInheritanceMap | O(files). Roslyn CSharpSyntaxTree.ParseText per file, 已替代 regex |
| SemanticTraversal | O(branches^depth). Bounded by MaxPaths (200) |
| Query Understanding | O(vocabulary_size × query_tokens). Lightweight |

## No Incremental Support

Current analysis is always full-scan. `GraphAnalysisScope` supports per-file incremental scoping but:
- No file system watcher integration
- No partial graph rebuild
- No scan.json caching

## No Parallelism

All analyzers run sequentially via `GraphAnalysisPipeline`. No parallel file processing, no multi-threaded syntax tree parsing.

## Target Framework

- **net8.0 only**. No netstandard2.0 or net48 compatibility.
- **Windows path handling**: Uses `Path.GetRelativePath()`, `Path.DirectorySeparatorChar`. Cross-platform with forward-slash normalization.

## Query Understanding Limitations

- **Chinese segmentation**: Simple 2-gram approach. No dictionary-based Chinese word segmentation (e.g., jieba).
- **Synonym coverage**: Fixed 18 Chinese↔English business concept pairs. No domain-adaptive expansion.
- **No embedding-based semantic matching**: Pure lexical + alias graph expansion. Cannot handle paraphrasing.

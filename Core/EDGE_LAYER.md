# Edge Layer — 边分层设计

## 概念

图中有且只有一种数据结构叫 `GraphEdge`，但通过 `Kind` 字段区分**语义层**。不同层的边遵循不同约束，由不同组件产生。

```
                     ┌─────────┐
                     │  Query   │ ← 对 QueryService 透明
                     │  (透明)   │   edges.Where(e => e.Kind == "call")
                     └────┬─────┘
                          │
     ┌────────────────────┼───────────────────────┐
     ▼                    ▼                       ▼
┌─────────┐   ┌──────────────────┐   ┌──────────────────┐
│  Call   │   │   Framework       │   │     Data          │
│  Layer  │   │   Layer           │   │     Layer         │
│ (基础)  │   │ (aspnet / spring) │   │ (ef / nh / sql)   │
└────┬────┘   └────────┬─────────┘   └────────┬─────────┘
     │                 │                       │
     ▼                 ▼                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                        GraphEdge.Kind                            │
└─────────────────────────────────────────────────────────────────┘
```

---

## Layer 1: Call (基础调用)

| 属性 | 说明 |
|------|------|
| Kind | `"call"` |
| 来源 | CodeGraphBuilder |
| 语义 | 方法 A 在代码中调用了方法 B |
| 产生时机 | Stage 3 Building |
| 边属性 | `analyzer` 不存在 |

```
Controller.GetById()
    │ call
    ▼
OrderService.FindById(int)
    │ call
    ▼
OrderRepository.GetById(int)
```

```json
{
  "fromId": "method:...::Controller.GetById()",
  "toId":   "method:...::OrderService.FindById(int)",
  "kind":   "call",
  "call":   "MyApp.Services.IOrderService.FindById",
  "isResolved": true
}
```

**特征**：
- CodeGraphBuilder 独占写入
- Analyzer 不产生 call 边
- 只读使用 (Analyzer 通过 Context.BaseGraph.Edges 获得)

---

## Layer 2: Framework (框架适配)

| Kind | Analyzer | 含义 |
|------|----------|------|
| `"spring:implements"` | SpringBeanAnalyzer | 接口方法 → 实现方法 |
| `"spring:property-ref"` | SpringBeanAnalyzer | Bean → 依赖 Bean |
| (计划) `"mediatr:handler"` | MediatRAnalyzer | Request → Handler |
| (计划) `"autofac:inject"` | AutofacAnalyzer | 依赖注入 |

### spring:implements

```
IOrderService.FindById(int)
    │ spring:implements
    ▼
OrderService.FindById(int)
```

```
{ "fromId": "method:...::IOrderService.FindById(int)",
  "toId":   "method:...::OrderService.FindById(int)",
  "kind":   "spring:implements",
  "label":  "IOrderService.FindById⇒OrderService.FindById",
  "isResolved": true,
  "attributes": { "analyzer": "spring-bean", "beanId": "orderService" }
}
```

### spring:property-ref

```
OrderService (bean) → OrderRepository (bean)
```

```
{ "fromId": "method:...::OrderService.FindById(int)",
  "toId":   "method:...::OrderRepository.GetById(int)",
  "kind":   "spring:property-ref",
  "label":  "spring:ref:orderRepository",
  "isResolved": true,
  "attributes": { "analyzer": "spring-bean", "propertyName": "Repository" }
}
```

**特征**：
- 由对应 Analyzer 产生
- 补全 call 层中「断头」的依赖关系
- 通常 `isResolved: true`
- 存储 `analyzer` 属性用于增量删除

---

## Layer 3: Data (数据访问)

| Kind | Analyzer | 含义 |
|------|----------|------|
| (计划) `"ef:query"` | EfSqlAnalyzer | Entity Framework 查询 |
| (计划) `"nh:query"` | NHibernateAnalyzer | NHibernate HQL |
| (计划) `"dapper:query"` | DapperAnalyzer | Dapper SQL |
| (计划) `"sql:plain"` | SqlAnalyzer | 原生 SQL |

### ef:query (计划)

```
OrderService.FindById(int)
    │ ef:query
    ▼
[SQL: SELECT * FROM Orders WHERE Id = @p0]
```

```
{ "fromId": "method:...::OrderService.FindById(int)",
  "toId":   "ext::sql::Orders.FindById",
  "kind":   "ef:query",
  "label":  "SELECT * FROM Orders WHERE Id = @p0",
  "isResolved": false,
  "attributes": {
    "analyzer": "ef-sql",
    "sql": "SELECT * FROM Orders WHERE Id = @p0",
    "provider": "SqlServer"
  }
}
```

**特征**：
- 来自 ORM 上下文分析
- 可能连接外部虚拟节点
- `isResolved: false` (SQL 语句不在图中)
- 存储 SQL 等数据在边属性中

---

## Layer 4: Transaction (事务边界)

| Kind | Analyzer | 含义 |
|------|----------|------|
| (计划) `"transaction"` | TransactionAnalyzer | 事务作用域 |
| (计划) `"uow:scope"` | UnitOfWorkAnalyzer | Unit of Work |

```
BeginTransaction()
    │ transaction:scope
    ▼ Add() ── Update() ── SaveChanges()
```

**特征**：
- 不一定是 A→B 的方法调用，可能是横切引用
- 边的属性携带事务隔离级别、超时等参数
- 用于审计和安全分析

---

## 跨层查询

Query 层对所有 Kind 透明：

```csharp
// 找到所有进入某方法的调用
var callers = query.GetCallers(methodId);
// 返回所有 Kind 的边对应的 caller

// 按 Kind 过滤
var frameworkEdges = graph.Edges
    .Where(e => e.Kind.StartsWith("spring:"));
```

---

## 边 Kind 命名约定

| 模式 | 示例 | 说明 |
|------|------|------|
| `call` | `"call"` | 唯一基础保留字 |
| `framework:relation` | `"spring:implements"` | 框架生成的关系 |
| `orm:query` | `"ef:query"` | ORM 数据查询 |
| `plain:type` | `"sql:plain"` | 原生类型 |
| `cross:type` | `"transaction:scope"` | 横切关注 |

---

## 增量删除

所有 Analyzer 产生的额外边必须携带 `analyzer` 属性：

```csharp
edge.Attributes["analyzer"] = "spring-bean";
edge.Attributes["sourceFile"] = "Config/spring.config";
```

增量时 MergeService 按 `analyzer + sourceFile` 精确清除：

```csharp
graph.Edges.RemoveAll(edge =>
    edge.Attributes.TryGetValue("analyzer", out var a)
    && a == "spring-bean"
    && InScope(edge.Attributes["sourceFile"]));
```

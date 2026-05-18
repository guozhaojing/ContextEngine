# Graph Model

## 总览

```
CodeGraph
├── ScanRoot
├── SchemaVersion
├── Nodes[]      → GraphNode
├── Edges[]      → GraphEdge
└── Facts[]      → GraphFact

每个节点的 Attributes 由 Analyze + Merge 写入
每条边的 Attributes 存储 analyzer / sourceFile 等元数据
每个 Fact 是独立的结构化事实
```

---

## GraphNode — 一个方法

```csharp
public class GraphNode
{
    string Id;                          // MethodId (主键)
    string Label;                       // "ClassName.MethodName(int, int)"
    string ProjectName;
    string ProjectPath;
    string Namespace;
    string ClassName;
    string MethodName;
    List<string> ParameterTypes;        // ["int", "int"]
    bool IsExternal;                    // 外部库方法
    List<string> CalledBy;              // 上游 caller Ids (反向物化)
    Dictionary<string, string> Attributes; // 分析注解
}
```

### IsExternal

- `false` — 当前解决方案内部的实现方法
- `true` — BCL / NuGet / 无法解析的方法（占位节点）

### CalledBy — 上游索引

由 `GraphAdjacencyMaterializer` 从 Edges 反向生成：

```
Edge: A → B
Node B.CalledBy 包含 "A"
```

QueryService.GetCallers(B) 直接返回 B.CalledBy。

### Attributes — 分析注解

格式: `"analyzer-name:key" = value`

| 示例 | 含义 |
|------|------|
| `"aspnet-route:route"` | `"/api/orders/{id}"` |
| `"aspnet-route:http-method"` | `"GET"` |
| `"aspnet-route:entry-point"` | `"true"` |
| `"spring-bean-id"` | `"orderService"` |
| `"spring-bean-type"` | `"MyApp.Services.OrderService, MyApp"` |

---

## GraphEdge — 一次调用关系

```csharp
public class GraphEdge
{
    string FromId;                      // 调用方 MethodId
    string ToId;                        // 被调方 MethodId
    string Call;                        // 显示字符串 "Namespace.Class.Method(int)"
    bool IsResolved;                    // 是否解析到内部节点
    string Kind;                        // 边的种类
    Dictionary<string, string> Attributes; // 元数据
}
```

### Kind 分类

| Kind | 来源 | 含义 |
|------|------|------|
| `"call"` | CodeGraphBuilder | 基础调用关系 (Roslyn 解析) |
| `"spring:implements"` | SpringBeanAnalyzer | 接口方法 → 实现方法 |
| `"spring:property-ref"` | SpringBeanAnalyzer | Bean → 依赖 Bean |
| `"ef:query"` | (计划) EfSqlAnalyzer | EF 查询 |
| `"nh:query"` | (计划) NHibernateAnalyzer | NHibernate 查询 |
| `"mediatr:handler"` | (计划) MediatRAnalyzer | Request → Handler |
| `"transaction"` | (计划) TransactionAnalyzer | 事务边界 |

### IsResolved

- `true` — ToId 对应 Nodes[] 中的内部节点
- `false` — ToId 对应外部库方法（无法纳入方案图）

### Attributes (边级)

与 `Kind` 互补，存放 analyzer / sourceFile 等元数据：

```json
{
  "analyzer": "spring-bean",
  "sourceFile": "Config/spring.config",
  "beanId": "orderService"
}
```

---

## GraphFact — 结构化事实

```csharp
public class GraphFact
{
    string Analyzer;                    // 分析器名称
    string SubjectId;                   // 关联主体 ID (通常是 methodId)
    string SubjectKind;                 // method | file | project | edge
    string FactType;                    // "http-route" | "spring-bean" | ...
    string? SourceFile;                 // 来源文件 (增量 key)
    Dictionary<string, string> Data;    // 自由键值对
}
```

### SubjectKind

| 值 | 含义 |
|------|------|
| `"method"` | 关联一个方法 |
| `"file"` | 关联一个源文件 |
| `"project"` | 关联一个项目 |
| `"edge"` | 关联一条边 |

### FactType 注册表

| FactType | Analyzer | 说明 |
|----------|----------|------|
| `"http-route"` | aspnet-route | Controller Action 路由 |
| `"spring-bean"` | spring-bean | Bean 定义 |
| `"ef-sql"` | (计划) | EF 生成的 SQL |
| `"nh-hql"` | (计划) | NHibernate HQL |

### data 结构 (http-route 示例)

```json
{
  "route": "/api/orders/{id}",
  "httpMethod": "GET",
  "controller": "OrdersController",
  "action": "GetById",
  "framework": "AspNetCore"
}
```

---

## GraphAnnotation — 节点注解

```csharp
public class GraphAnnotation
{
    string Analyzer;                    // 分析器名称
    string TargetMethodId;              // 目标方法 MethodId
    string Key;                         // 键 (不含前缀)
    string Value;                       // 值
    string? SourceFile;                 // 来源文件
}
```

### Merge 写入规则

Analyzer 产出 GraphAnnotation{ Key: "route", Value: "/api/orders" }

Merge 写入:
```
GraphNode.Attributes["aspnet-route:route"] = "/api/orders"
GraphNode.Attributes["_sourceFile"] = "src/Controllers/OrdersController.cs"
```

如果 Key 已含 `:` 前缀，则直接使用（不做二次加前缀）。

---

## GraphExtraEdge — 分析器产生的额外边

```csharp
public class GraphExtraEdge
{
    string FromId;                      // 源 MethodId
    string ToId;                        // 目标 MethodId
    string Kind;                        // 边类型
    string Label;                       // 显示标签
    bool IsResolved;                    // 目标是否在图内
    string? SourceFile;                 // 来源文件
    Dictionary<string, string> Attributes; // 元数据
}
```

### ExtraEdge → GraphEdge 转换

MergeService 将 GraphExtraEdge 转换为 GraphEdge，并自动添加 `analyzer` 和 `sourceFile` 属性。

---

## 完整数据关系

```
┌────────────────────────────────────────────────────────────────────┐
│ CodeGraph                                                          │
│                                                                    │
│  GraphNode                          GraphEdge                      │
│  ┌──────────────────┐              ┌──────────────────────┐        │
│  │ Id = "method:.." ├──────────────┤ FromId = "method:.." │        │
│  │ Label            │   call 边    │ ToId   = "method:.." │        │
│  │ IsExternal       │              │ Kind   = "call"      │        │
│  │ CalledBy ←───────┤←──反向物化───│ Attributes           │        │
│  │ Attributes ──────┤←──Merge写入──│                      │        │
│  └──────────────────┘   extra 边   └──────────────────────┘        │
│                                                                    │
│  GraphFact                                                         │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │ Analyzer = "aspnet-route"                                 │      │
│  │ SubjectId = "method:...::OrdersController.GetById(int)"  │      │
│  │ FactType = "http-route"                                   │      │
│  │ Data = { "route": "/api/orders/{id}", ... }              │      │
│  └──────────────────────────────────────────────────────────┘      │
└────────────────────────────────────────────────────────────────────┘
```

# ContextEngine Analyzer Guide

## 1. 架构概览

```
Program.cs (编排层)
  └── CodeGraphAnalysisOrchestrator(new IGraphAnalyzer[] { ... })
          │
          ├── CodeGraphBuilder.Build(scan)         基础调用图 (不可变)
          │
          ├── GraphAnalysisPipeline.Run(scan, graph)
          │     └── foreach analyzer:
          │           ctx = GraphAnalysisContext.Create(scan, graph, scope, analyzer)
          │           analyzer.Analyze(ctx)          每个 Analyzer 独立 Context
          │           → GraphAnalysisResult          产出 Facts/Annotations/ExtraEdges
          │
          └── GraphAnalysisMergeService.Merge(graph, results, scope)
                ├── CloneGraph(baseGraph)            深拷贝
                ├── RemoveAnalyzerContributions()    增量：清理旧数据
                ├── ApplyFacts / Annotations / Edges  写入
                └── GraphAdjacencyMaterializer + GraphIndex.Build  重建索引
```

## 2. 创建 Analyzer 步骤

### Step 1: 新建类文件

```
Core/Graph/Analysis/Analyzers/
├── AspNetRouteAnalyzer.cs      示例 1: 框架适配
├── SpringBeanAnalyzer.cs       示例 2: 配置文件解析
└── YourNewAnalyzer.cs          ← 新建
```

### Step 2: 实现接口

```csharp
public sealed class YourNewAnalyzer : IGraphAnalyzer
{
    public string Name => "your-unique-name";

    public void Analyze(GraphAnalysisContext context)
    {
        // 1. 从 context 读取数据
        // 2. 分析产生结论
        // 3. 通过 context.AddFact/AddAnnotation/AddExtraEdge 产出
    }
}
```

### Step 3: 注册

```csharp
// Program.cs
var orchestrator = new CodeGraphAnalysisOrchestrator(new IGraphAnalyzer[]
{
    new AspNetRouteAnalyzer(),
    new SpringBeanAnalyzer(),
    new YourNewAnalyzer()    // ← 注册
});
```

## 3. Context API

`GraphAnalysisContext` 是 Analyzer 唯一的输入来源和输出通道：

### 可读取

| 属性 | 类型 | 说明 |
|------|------|------|
| `Scan` | `SolutionScanResult` | 扫描结果，含全部 CodeUnit |
| `BaseGraph` | `IGraphSnapshot` | 基础调用图只读快照 |
| `Scope` | `GraphAnalysisScope` | 分析范围（全量/增量） |
| `Result` | `GraphAnalysisResult` | 当前 Analyzer 输出容器 |
| `NodesById` | `IReadOnlyDictionary<string, GraphNode>` | 节点 ID → 节点 |
| `UnitsByFile` | `ILookup<string, CodeUnit>` | 文件相对路径 → CodeUnits |

### 可写入（仅通过以下方法）

| 方法 | 产出 | 最终落点 |
|------|------|---------|
| `AddFact(subjectId, factType, subjectKind, sourceFile, data)` | `GraphFact` | `CodeGraph.Facts` |
| `AddAnnotation(methodId, key, value, sourceFile)` | `GraphAnnotation` | `GraphNode.Attributes["analyzerName:key"]` |
| `AddExtraEdge(fromId, toId, kind, label, isResolved, sourceFile, attributes)` | `GraphExtraEdge` | `CodeGraph.Edges` |

### 便捷方法

| 方法 | 说明 |
|------|------|
| `GetUnitsInScope()` | 返回 Scope 范围内的 CodeUnit 枚举 |

## 4. 产出类型详解

### GraphFact — 结构化事实

```json
{
  "analyzer": "your-analyzer-name",
  "subjectId": "method:src/...::Namespace.Class.Method(int)",
  "subjectKind": "method",
  "factType": "your-fact-type",
  "sourceFile": "src/Services/SomeFile.cs",
  "data": {
    "key1": "value1",
    "key2": "value2"
  }
}
```

- 每个事实关联一个主体（methodId / file / project / edge）
- `data` 是自由键值对，由 Analyzer 定义结构
- `sourceFile` 用于增量分析——不指定则全局有效

### GraphAnnotation — 节点注解

```json
{
  "analyzer": "your-analyzer-name",
  "targetMethodId": "method:...",
  "key": "your-key",
  "value": "your-value"
}
```

- 最终写入 `GraphNode.Attributes["analyzerName:key"] = value`
- 支持 Query 层按 key 过滤：`node.Attributes["aspnet-route:entry-point"]`
- Merge 层自动加 analyzer 前缀

### GraphExtraEdge — 额外边

```json
{
  "fromId": "method:...::Service.Call()",
  "toId": "method:...::Repository.Query()",
  "kind": "your-edge-kind",
  "label": "description",
  "isResolved": true
}
```

- 最终写入 `CodeGraph.Edges`，会被索引和遍历
- `kind` 用于过滤：`graph.Edges.Where(e => e.Kind == "spring:implements")`
- `isResolved=true` 表示目标在图中存在

## 5. 最佳实践

### ✅ DO

- 使用 `seen` HashSet 防止重复产出
- 在 `context.NodesById.ContainsKey(methodId)` 确认节点存在
- 将 `sourceFile` 传递给每个 Fact/Annotation/Edge 以支持增量
- 使用 `context.UnitsByFile` 快速按文件查找 CodeUnit
- 使用 `MethodIdBuilder.FromMethod()` 构造稳定的 Id
- 使用 `GraphSubjectKinds` 常量：`Method`, `File`, `Project`, `Edge`
- 按 `analyzerName + sourceFile` 维度组织产出，便于增量清理
- 保持 Analyzer 无状态——所有状态在单次 `Analyze` 调用内

### ❌ DON'T

- 不依赖 `GraphQueryService`
- 不修改 `CodeGraphBuilder`
- 不直接修改 `GraphNode` 的任何属性
- 不跨 Analyzer 通信（应通过 Facts 作为公共通道）
- 不引入数据库或文件持久化
- 不引入静态可变状态
- 不修改 `MethodId` 生成规则

## 6. 增量分析

增量分析对 Analyzer **完全透明**——Analyzer 无需任何特殊处理：

```
全量扫描:
  scope = GraphAnalysisScope.Full()
  → Pipeline 传递完整 Context
  → Analyzer 正常产出
  → Merge 写入所有产出

增量扫描:
  scope = GraphAnalysisScope.ForFiles(["src/Changed.cs"])
  → Pipeline 仍传递完整 Context
  → Analyzer 仍正常产出（但可调用 context.GetUnitsInScope() 优化）
  → Merge 先按 analyzer + sourceFile 清除旧产出
  → Merge 再写入新产出
  → 未改文件的数据不受影响
```

只需保证产出时传入 `sourceFile` 参数即可。

## 7. Analyzer 间隔离机制

| 隔离维度 | 机制 |
|----------|------|
| 执行隔离 | 每个 Analyzer 获得独立的 `GraphAnalysisContext` 和 `GraphAnalysisResult` |
| 存储隔离 | Annotation 自动加前缀 `"analyzerName:key"` |
| 增量隔离 | Merge 按 `analyzerName + sourceFile` 精确删除 |
| 类型隔离 | 各自位于独立文件，互不 import |

## 8. 已实现的 Analyzer

### AspNetRouteAnalyzer (`"aspnet-route"`)

| 项目 | 说明 |
|------|------|
| 目标 | ASP.NET MVC / Core Controller |
| 识别 | `[ApiController]`, 类名 `XxxController`, BaseType `Controller`/`ControllerBase` |
| HTTP | `[HttpGet]`/`[HttpPost]`/... 属性 + 命名约定回退 |
| 路由 | `[Route("api/[controller]")]` + `[HttpGet("{id}")]` 拼接，支持 token 替换 |
| Fact | `factType: "http-route"` — route, httpMethod, controller, action, framework |
| Annotation | `aspnet-route:route`, `aspnet-route:http-method`, `aspnet-route:entry-point` |

### SpringBeanAnalyzer (`"spring-bean"`)

| 项目 | 说明 |
|------|------|
| 目标 | Spring.NET XML 配置 |
| 发现 | 扫描 `scanRoot` 下 `*.xml`/`*.config`，查找 `<objects>` 元素 |
| Bean | `<object id="..." type="...">` 解析 |
| 依赖 | `<property name="..." ref="...">` 建立 Bean 间关系 |
| 接口映射 | 解析 Bean 类源码 → `class Impl : IInterface` |
| Fact | `factType: "spring-bean"` |
| Edge | `spring:implements` (接口方法→实现方法), `spring:property-ref` (Bean→Bean) |

### NHibernateAnalyzer (`"nh-hql"`)

| 项目 | 说明 |
|------|------|
| 目标 | NHibernate Session API 直接调用 + HQL 字符串追踪 |
| 识别 | `session.Query<T>()`, `session.Save()`, `session.Get<T>()` 等 26 种 API |
| HQL | 从字符串字面量提取实体名、SQL FROM 子句 |
| HBM | 解析 `.hbm.xml` 获取 Entity↔Table 映射 |
| Fact | `factType: "nh-entity-access"`, `"nh-hql"`, `"nh-sql"` |
| Edge | `nh:entity-access` (method → Entity Node) |
| Annotation | `entity`, `table`, `api` |

### NhSessionGenericAnalyzer (`"nh-generic-resolution"`)

| 项目 | 说明 |
|------|------|
| 目标 | 泛型继承链 + Repository/DAO 模式解析 |
| 识别 | `class X : BaseBLL<T>` → T=Entity, `class Y : BaseDaoNHB<T,T1>` → T=Entity |
| 继承映射 | **Roslyn SyntaxTree** 解析所有 class/interface 声明，构建继承树 |
| 调用解析 | **Roslyn InvocationExpressionSyntax** 提取方法体泛型调用 |
| 字段检测 | **Roslyn FieldDeclarationSyntax** 检测 BLL 中的 DAO 字段 |
| 调用传播 | **Roslyn MemberAccessExpressionSyntax** 追踪 `_dao.Method()` 调用 |
| 置信度 | 5 级：Exact / High / Medium / Low / None |
| Fact | `factType: "nh-entity-access"` (带 `viaClass`, `resolution`, `generic:resolved`) |
| Edge | `nh:entity-access` (method → ext::nh:entity::{NS}.{Class}::{Table}) |
| Annotation | `generic:resolved`, `entity`, `table`, `api` |
| 诊断 | unresolved-generic-binding, ambiguous-generic-binding, duplicate-entity-source, orphan-propagation-edge |
| 文件 | `Core/Graph/Analysis/GenericResolution/` (12 文件) |

## 9. 计划中的 Analyzer

| Analyzer | 目标 | 建议 factType / kind |
|----------|------|---------------------|
| EfSqlAnalyzer | Entity Framework / EF Core | `"ef-sql"` |
| MediatRAnalyzer | MediatR Request/Handler | `"mediatr:handler"` 边 |
| DapperAnalyzer | Dapper SQL 字符串 | `"dapper-sql"` |

## 10. 调试

```bash
# 查看某次扫描的 Facts
jq '.facts' graph-*.json

# 按 Analyzer 过滤
jq '.facts[] | select(.analyzer == "aspnet-route")' graph-*.json

# 查看带注解的节点
jq '.nodes[] | select(.attributes | keys[] | startswith("aspnet-route"))' graph-*.json

# 查看额外边
jq '.edges[] | select(.kind == "spring:implements")' graph-*.json
```

## 11. 文件索引

```
Core/Graph/Analysis/
├── IGraphAnalyzer.cs                   接口定义
├── GraphAnalysisContext.cs             只读上下文 + IGraphSnapshot
├── GraphAnalysisResult.cs              单 Analyzer 产出容器
├── GraphFact.cs                        结构化事实 + GraphSubjectKinds
├── GraphAnnotation.cs                  节点注解
├── GraphExtraEdge.cs                   额外边
├── GraphAnalysisPipeline.cs            分析器管道（顺序执行）
├── GraphAnalysisRunResult.cs           管道汇总
├── GraphAnalysisScope.cs               全量 / 增量范围
├── GraphAnalysisMergeService.cs        合并入图（深拷贝 + 增量清理）
├── CodeGraphAnalysisOrchestrator.cs    编排入口
├── Analyzers/
│   ├── AspNetRouteAnalyzer.cs          ASP.NET 路由分析
│   ├── SpringBeanAnalyzer.cs           Spring.NET Bean 分析
│   └── NHibernateAnalyzer.cs           NHibernate HQL / Session API 分析
└── GenericResolution/
    ├── GenericInheritanceMap.cs         类继承映射 (Roslyn SyntaxTree)
    ├── GenericTypeResolver.cs           泛型类型参数解析
    ├── GenericInvocationResolver.cs     泛型调用解析 (Roslyn)
    ├── DaoFieldDetector.cs              DAO 字段检测 (Roslyn)
    ├── DaoCallSiteResolver.cs           BLL→DAO 调用传播 (Roslyn)
    ├── EntityClassRegistry.cs           Entity↔Class 双向映射
    ├── RepositoryPatternDetector.cs     Repository 模式检测
    ├── NhSessionGenericAnalyzer.cs      泛型解析编排 (IGraphAnalyzer)
    ├── GenericResolutionResult.cs       结果收集 + Origin Trace
    ├── GenericResolutionConfidence.cs   5 级置信度
    ├── GenericEntityAccessFact.cs       泛型 Entity Access 事实
    └── NamePatterns.cs                 命名模式工具
```

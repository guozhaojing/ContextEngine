# MethodId — 稳定方法标识

## 为什么需要稳定 MethodId

- MethodId 是图中所有 Node 的主键
- MethodId 是所有 Edge 的 FromId / ToId
- MethodId 是所有 Analyzer 产出 Fact / Annotation / ExtraEdge 的关联键
- 增量扫描时，同一方法的 Id 必须不变，否则旧边/旧 Fact 无法清理

**→ 所有 Analyzer 依赖它对齐数据。**

---

## 生成规则

### 内部方法

```
method:{项目相对路径}::{命名空间.类名.方法名(参数类型列表)}
```

| 组成部分 | 来源 | 示例 |
|----------|------|------|
| `method` | 固定前缀 | `method` |
| `{项目相对路径}` | csproj 相对扫描根路径 | `src/Api/Api.csproj` |
| `{命名空间.类名.方法名}` | Roslyn SyntaxTree | `MyApp.Services.OrderService.GetById` |
| `(参数类型列表)` | Roslyn SyntaxTree | `(int, string)` |

#### 完整示例

```
method:src/api/api.csproj::MyApp.Services.OrderService.GetById(int)
method:src/api/api.csproj::MyApp.Services.OrderService.GetByName(string)
```

### 外部方法

```
ext::{命名空间.类名.方法名(参数类型列表)}
```

| 组成部分 | 来源 | 示例 |
|----------|------|------|
| `ext` | 固定前缀 | `ext` |
| `{命名空间.类名.方法名}` | IMethodSymbol.ToDisplayString() | `System.Console.WriteLine` |
| `(参数类型列表)` | IMethodSymbol.Parameters | `(string, object[])` |

#### 示例

```
ext::System.Console.WriteLine(string)
ext::Microsoft.EntityFrameworkCore.DbContext.SaveChanges()
```

---

## 稳定规则

### ✅ 不影响 Id 稳定性的变更

| 操作 | MethodId 是否变 |
|------|----------------|
| 源文件移动（同一项目内） | 不变 |
| 项目重命名 | 变 (csproj 路径变) |
| 添加/删除 using | 不变 |
| 类内重排方法顺序 | 不变 |
| 方法重构 (改名) | 变 (methodName 变) |
| Partial class 拆分/合并 | 不变 (同名同参) |
| 项目目录移动 | 不变 (路径相对于扫描根) |

### 关键设计决策

```
不依赖源文件路径  → 文件移动不影响
不依赖行号        → 代码插入不影响
包含参数类型      → 重载方法可区分
包含项目路径      → 不同项目的同名方法可区分
项目路径使用相对  → 整个仓库移动不影响 (只要相对结构不变)
```

---

## API

```csharp
// 从 CodeUnit 生成
MethodIdBuilder.FromCodeUnit(unit)
// → 调用 FromMethod(unit.ProjectPath, unit.Namespace, unit.ClassName, unit.MethodName, unit.ParameterTypes)

// 手动构造
MethodIdBuilder.FromMethod("src/api/api.csproj", "MyApp", "OrderService", "Get", new[] { "int" })
// → "method:src/api/api.csproj::MyApp.OrderService.Get(int)"

// 从 ResolvedMethodInfo 生成 (调用目标)
MethodIdBuilder.FromResolvedMethod(resolved, sourceProjectPath)
// → FromMethod 或 ForExternal

// 外部方法
MethodIdBuilder.ForExternal(resolved)
// → "ext::System.Console.WriteLine(string)"

// 归一化
MethodIdBuilder.NormalizeProjectPath("src\\Api\\Api.csproj")
// → "src/api/api.csproj"

// 限定名
MethodIdBuilder.BuildQualifiedName("MyApp", "OrderService", "Get", new[] { "int" })
// → "MyApp.OrderService.Get(int)"
```

---

## Analyzer 使用 MethodId

每个 Analyzer 必须能构造出稳定的 MethodId 来关联分析结果：

### AspNetRouteAnalyzer

```csharp
var methodId = MethodIdBuilder.FromMethod(
    projectPath,          // 从 CodeUnit 获取
    namespaceName,        // 从 SyntaxTree 获取
    className,            // 从 SyntaxTree 获取
    methodName,           // 从 SyntaxTree 获取
    parameterTypes);      // 从 MethodDeclarationSyntax.ParameterList 提取
```

### SpringBeanAnalyzer

```csharp
// Bean 类型映射
// type "MyApp.Services.OrderService, MyApp" → Namespace + ClassName
// 找匹配的 CodeUnit → 得到 MethodId
```

---

## 方法重载处理

```
Get(int id)      →  method:...::Service.Get(int)
Get(string name) →  method:...::Service.Get(string)
```

两个不同方法 → 两个不同 MethodId → 两个独立 GraphNode。

如果没有参数类型：
- 两个 Node 会因 Id 冲突而合并
- 边会指向错误的 Node
- 查询结果不正确

**→ 参数类型是 MethodId 的必要组成部分。**

---

## 格式限制

- `namespaceName` 为空：直接 `ClassName.MethodName` (跳过命名空间段)
- `parameterTypes` 为空：不追加 `()` 后缀
- `projectPath` 始终 normalzie：`\` → `/`，全小写，trim
- 外部方法无 projectPath，使用 `ext::` 前缀

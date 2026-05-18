# ContextEngine 架构速览

## 程序在做什么？

1. **扫描**（Scanning）：遍历 .NET 解决方案里的 `.cs` 文件，找出每个**类**里的每个**方法**，以及方法里**调用了谁**。
2. **语义解析**（Semantics）：用 Roslyn 的 `SemanticModel.GetSymbolInfo` 把 `foo.Bar()` 解析成真实的命名空间/类/方法（不是简单字符串匹配）。
3. **建图**（Graph.Building）：把方法变成**节点**，把调用关系变成**边**（A 调用 B）。
4. **分析管道**（Graph.Analysis）：可插拔的分析器，往图上追加 Facts / 注解 / 额外边（以后 ASP.NET、EF 等走这里）。
5. **查询**（Graph.Query）：问图——谁调用了我？我调用了谁？调用链？入口方法？

## 数据流（一次扫描）

```
用户输入路径
  → ProjectCodeScanner.ScanAsync()     产出 SolutionScanResult（含 CodeUnit 列表）
  → CodeUnitJsonExporter               保存 scan-*.json
  → CodeGraphAnalysisOrchestrator
       ├ CodeGraphBuilder.Build()      基础调用图 + GraphIndex
       ├ GraphAnalysisPipeline.Run()    各 IGraphAnalyzer（当前可为空）
       └ GraphAnalysisMergeService      合并分析结果，重建索引
  → CodeGraphJsonExporter              保存 graph-*.json
  → GraphQueryService                  内存查询
```

## 目录说明

| 目录 | 作用 |
|------|------|
| `Core/Models` | `CodeUnit` 扫描结果的一条记录（一个方法） |
| `Core/Scanning` | 发现 .sln/.csproj、解析语法树、填充 CodeUnit |
| `Core/Semantics` | 仅做 Roslyn 符号解析，不含图逻辑 |
| `Core/Graph/Identity` | 稳定的 `MethodId` 生成规则 |
| `Core/Graph/Building` | 从 CodeUnit 建节点和边 |
| `Core/Graph/Indexing` | 邻接索引、CalledBy 物化 |
| `Core/Graph/Traversal` | 防环遍历工具 |
| `Core/Graph/Query` | 图查询 API |
| `Core/Graph/Analysis` | 分析器插件与合并 |
| `Core/Export` | 导出 JSON 文件 |

## MethodId 格式

- 内部方法：`method:{项目相对路径}::{命名空间.类名.方法名}`
- 外部方法：`ext::{限定名}`

同一方法在增量扫描时 Id 不变，便于以后做差量更新。

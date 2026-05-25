# Current State

## Project Identity

ContextEngine — 确定性、接地验证的语义运行时，用于可信赖的代码智能。
目标框架 .NET 9.0（原 8.0），入口点 Program.cs 支持三种模式。

## Statistics (from real scan: ZhiFang.IEQA.Platform.Core)

| Metric | Value |
|---|---|
| Projects | 25+ |
| CodeUnits (methods) | ~2,500 |
| Graph nodes | ~4,400 |
| Graph edges | ~17,800 |
| Facts | ~4,900 |

## Subsystems (18 total, 6 layers)

### 数据采集层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Scanning` | ✅ | Roslyn 项目扫描，CodeUnit 生成 |

### 图构建与查询层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Graph` | ✅ | 调用图构建、索引、遍历、查询 |
| `Core.Graph.Analysis` | ✅ | NHibernate/Spring/ASP.NET 分析器 |
| `Core.Graph.Analysis.GenericResolution` | ✅ | 泛型解析（Roslyn 替代 regex） |
| `Core.Graph.Identity` | ✅ | 稳定 MethodId |
| `Core.Graph.Query` | ✅ | 语义遍历引擎 |

### 语义与真值层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Semantics` | ✅ | SymbolHandle、引用索引、符号图 |
| `Core.Truth` | ✅ | TruthScore 统一真值模型 |

### 接地执行层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Grounding` | ✅ | 声明验证、幻觉阻止、引用约束 |
| `Core.Grounding.Confidence` | ✅ | 确定性置信度传播、8级衰减规则 |
| `Core.Grounding.Contradictions` | ✅ | 8种矛盾检测、一致性验证 |

### 认知引擎层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Cognition` | ✅ | 四大引擎：架构/影响/能力/根因 |
| `Core.Cognition.Epistemics` | ✅ | 认知边界、三维置信度、6种证据状态 |
| `Core.Cognition.Patching` | ✅ | 解释→计划→补丁、约定检测 |
| `Core.Cognition.CodeFix` | ✅ | 代码修复流水线（LLM+Build+重试） |

### 自验证层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.SelfValidation` | ✅ | 5维自评、6种风险检测、自我批评 |
| `Core.Verification` | ✅ | 5维可信度验证、6级裁定 |

### 运行时层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Runtime` | ✅ | 语义状态、溯源快照、回放指纹 |
| `Core.Runtime.Governance` | ✅ | 9个不变式、状态转换验证、漂移检测 |

### 语义文档层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Cognition.SemanticDoc` | ✅ | 方法语义文档、反向索引、嵌入服务、混合检索、上下文压缩 |
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Experience` | ✅ | 仓库会话、查询路由、交互式对话 |
| `Core.ReasoningUX` | ✅ | 5层渐进式推理呈现 |
| `Core.Observability` | ✅ | 系统地图、架构叙述、复杂度分析 |
| `App.Cli` | ✅ | 交互式 REPL（14个命令） |
| `App.WebApi` | ✅ | ASP.NET Core REST API + WebUI 后端 |

### 评估层
| Namespace | Status | Purpose |
|---|---|---|
| `Core.Evaluation.Cognition` | ✅ | 12个基准场景、4种工作流模拟、回归测试 |
| `Core.Explainability` | ✅ | 审计追踪、证据报告、排序解释 |

## Analyzer Registry

| Analyzer | Edge Kinds | Facts |
|---|---|---|
| `AspNetRouteAnalyzer` | (annotations) | http-route |
| `SpringBeanAnalyzer` | spring:implements, spring:property-ref | spring-bean |
| `NHibernateAnalyzer` | nh:entity-access | nh-entity-access, nh-hql |
| `NhSessionGenericAnalyzer` | nh:entity-access | nh-entity-access (generic) |
| `SpringContextObjectAnalyzer` | spring:object-get, spring:property-inj, spring:generic-dao | — |

## Key Edge Kinds (complete)

| Kind | Purpose |
|---|---|
| `call` | 直接调用 |
| `nh:entity-access` | NHibernate 实体访问 |
| `spring:implements` | Spring 接口实现 |
| `spring:property-ref` | Spring XML 属性引用 |
| `spring:object-get` | context.GetObject() 解析 |
| `spring:property-inj` | XML 属性注入 |
| `spring:generic-dao` | 泛型基类 DAO 调用穿透 |

## Dependency Edge Types

| Type | Risk Adjust |
|---|---|
| DirectCall | 0 |
| PrivateImplementation | -0.2 |
| InterfaceContract | -0.1 |
| EntryPointReachable | +0.05 |
| TransitiveCall | +0.1 |

## API Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/session` | 会话状态 |
| POST | `/api/load` | 加载仓库 |
| POST | `/api/reload` | 重新扫描 |
| POST | `/api/agent` | 统一 Agent（查询+修改） |
| GET | `/api/history` | 仓库加载历史 |
| DELETE | `/api/history` | 删除历史条目 |
| GET | `/api/evidence/{id}` | 节点证据详情 |
| GET | `/api/observability/map` | 子系统地图 |
| GET | `/api/observability/health` | 架构健康度 |
| POST | `/api/cache/clear` | 清除缓存 |

## Confidence Model

```
TruthSource × EvidenceStrength = TruthScore (0.0-1.0)
  → GroundingConfidence: Certain/Strong/Moderate/Weak/Speculative/Unsupported
  → Edge decay: DirectSymbolBinding×1.00 ... SpeculativeExpansion×0.40
  → 6 trustworthiness verdicts: StronglyGrounded ... RequiresFurtherInvestigation
```

## Build Status

- `dotnet build`: 0 errors, 1 harmless nullable warning
- Target: .NET 9.0
- Frontend: React 18 + Tailwind CSS 3 + Vite

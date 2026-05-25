# 架构概览

## 系统层级

```
App/Cli/          ← 开发者 REPL (14个命令)
App/WebApi/       ← REST API + 缓存会话管理
  ↓
Core/Experience/  ← 会话管理、查询路由、格式化
Core/RelaxationUX/ ← 渐进式推理呈现 (5层披露)
  ↓
Core/Cognition/   ← 架构、影响、能力、根因 四大引擎
Core/Cognition/Epistemics/ ← 认知边界、三维置信度
Core/Cognition/Patching/   ← 解释→计划→补丁
  ↓
Core/SelfValidation/ ← 响应自评、风险分析、自我批评
Core/Verification/   ← 5维可信度验证、6级裁定
  ↓
Core/Grounding/     ← 声明验证、幻觉阻止、引用约束
Core/Grounding/Confidence/      ← 确定性置信度传播
Core/Grounding/Contradictions/  ← 矛盾检测、一致性验证
  ↓
Core/Runtime/       ← 语义状态、回放指纹、溯源快照
Core/Runtime/Governance/ ← 不变式注册、状态转换验证
  ↓
Core/Graph/         ← 代码图、索引、遍历、查询
Core/Graph/Analysis/← NHibernate, Spring, AspNet 分析器
  ↓
Core/Semantics/     ← Roslyn 符号绑定、SymbolHandle
Core/Truth/         ← 统一真值评分、证据强度
  ↓
Core/Scanning/      ← Roslyn 项目扫描、CodeUnit 提取
Core/Observability/ ← 系统地图、架构叙述、复杂度分析
```

## 关键命名空间

| 命名空间 | 用途 |
|---|---|
| `Core.Scanning` | 发现 .NET 项目，扫描 C# 源码 |
| `Core.Graph` | 构建调用图，索引邻接，语义遍历 |
| `Core.Graph.Analysis` | NHibernate, Spring.NET, ASP.NET 路由分析器 |
| `Core.Semantics` | Roslyn 符号绑定 (SymbolHandle), 引用索引 |
| `Core.Truth` | TruthScore — 统一置信度/证据模型 |
| `Core.Grounding` | 声明验证、幻觉阻止、引用生成 |
| `Core.Grounding.Confidence` | 置信度传播、边缘衰减规则 |
| `Core.Grounding.Contradictions` | 矛盾检测、一致性验证、矛盾感知生成 |
| `Core.Runtime` | 语义状态、回放指纹、溯源快照 |
| `Core.Runtime.Governance` | 不变式注册、状态转换验证、漂移检测 |
| `Core.Cognition` | 架构/影响/能力/根因探索器 |
| `Core.Cognition.Epistemics` | 认知边界、三维置信度、证据状态分类 |
| `Core.Cognition.Patching` | 约定分析、补丁规划、接地代码生成 |
| `Core.SelfValidation` | 5维响应自评、6种风险检测、自我批评 |
| `Core.Verification` | 5维可信度验证、6级裁定 |
| `Core.ReasoningUX` | 渐进式推理呈现、工程摘要合成 |
| `Core.Experience` | 仓库会话、查询路由、交互式会话 |
| `Core.Observability` | 系统地图生成、架构叙述、复杂度分析 |
| `App.Cli` | REPL, 缓存, CLI 工具 |
| `App.WebApi` | REST API 端点, WebUI 后端 |

## 认知流水线

```
Query → DeveloperQueryInterpreter → QueryRouter
  → ArchitectureExplorer / ChangeImpactAnalyzer / BusinessCapabilityMapper / GroundedRootCauseExplorer
  → CognitionResult
  → EpistemicBoundary → EpistemicReport
  → ResponseSelfEvaluator → SelfEvaluation
  → EpistemicRiskAnalyzer → EpistemicRiskReport
  → InvestigationGapDetector → InvestigationGapReport
  → SelfCritiqueGenerator → SelfCritique
  → VerificationOrchestrator → VerificationReport
  → ReasoningPresentationEngine → ProgressiveResponse (5层渐进披露)
  → 用户可见响应
```

## 确定性架构

| 层 | 确定性机制 |
|---|---|
| 标识 | MethodId — 内容派生的稳定 ID |
| 符号 | SymbolHandle — Roslyn DocumentationCommentId |
| 迭代 | 所有集合上 OrderBy(StringComparer.Ordinal) |
| 排序 | DeterministicRanker — 多键排序 + 终端平局键 |
| 传播 | 固定衰减规则, BFS + 排序扩展 |
| 状态 | 不可变 readonly struct / sealed init-only 类 |
| 回放 | 所有状态类型实现 IEquatable<T>, ReplayFingerprint |
| 治理 | 机器可验证不变式, 静态验证规则 |

## 置信度模型

```
TruthSource (Roslyn > NHibernate > Spring > Analyzer > Heuristic)
  × EvidenceStrength (SemanticDirect > SemanticInferred > SyntaxDirect > SyntaxPattern)
  = TruthScore (0.0–1.0)
  → GroundingConfidence (Certain/Strong/Moderate/Weak/Speculative/Unsupported)
```

边缘衰减率:
- DirectSymbolBinding × 1.00
- ExplicitInvocation × 0.95
- ControlFlow × 0.92
- DataFlow × 0.90
- Inheritance × 0.88
- ConfigurationBinding × 0.85
- SemanticSimilarity × 0.75
- PropagationInference × 0.60
- SpeculativeExpansion × 0.40

## 可信度裁定

| 裁定 | 可信任? |
|---|---|
| StronglyGrounded | ✅ 高度可信 |
| PartiallyVerified | ⚠️ 审慎使用 |
| RuntimeIncomplete | ⚠️ 动态行为不明 |
| CompetingHypothesesPresent | ❌ 存在竞争解释 |
| LimitedEvidence | ❌ 证据不足 |
| RequiresFurtherInvestigation | ❌ 需更多分析 |

## 自验证层

```
5维评分: 接地(30%) + 覆盖(25%) + 校准(20%) + 假设(10%) + 可用(15%)
  → Approved / Qualified / NeedsReview / Rejected
```

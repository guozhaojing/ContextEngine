# ContextEngine

确定性、溯源可追溯、接地验证的语义运行时，用于可信赖的代码智能。

## 核心能力

| 能力 | 说明 |
|---|---|
| **架构理解** | 识别子系统边界、分层、集成点 |
| **变更影响分析** | 追踪下游影响、上游依赖、风险评分 |
| **业务能力映射** | 将服务类映射为业务能力，追踪执行链路 |
| **根因分析** | 失败路径分析、外部依赖检测、交互失败点 |
| **认知边界** | 区分已确认存在/不存在、运行时未解析、分析不完整 |
| **自我验证** | 5维响应质量自评、6种认知风险检测、诚实自我批评 |
| **可信度验证** | 6级可信度裁定（StronglyGrounded 到 RequiresFurtherInvestigation） |
| **渐进式呈现** | 5层渐进披露：工程摘要 → 证据 → 不确定性 → 自评 → 调查建议 |
| **解释→计划→补丁** | 约定检测、影响评估、遵循仓库约定的代码生成 |

## 系统架构

```
数据采集 → 图构建 → 语义分析 → 接地与置信 → 认知引擎 → 自我验证 → 呈现
```

共 18 个子系统，6 个架构层。详见 `docs/ARCHITECTURE_OVERVIEW.md`。

## 快速开始

```bash
# Web 认知工作台（推荐）
dotnet run -- --web
# 另开终端: cd webui && npm install && npm run dev
# 浏览器: http://localhost:5173

# 命令行 REPL
dotnet run
cognition> load D:\Projects\MySolution
cognition> ask "解释架构"
cognition> verify "改动 RetryPolicy 影响"
cognition> self-critique "为什么重试失败"
cognition> patch "在 PaymentService 加日志"
cognition> map          # 查看认知流水线
cognition> health       # 检查架构健康度
```

## REPL 命令

| 命令 | 说明 |
|---|---|
| `load <路径>` | 加载 .NET 解决方案 |
| `ask <问题>` | 自然语言查询（自动路由） |
| `followup <问题>` | 追问 |
| `impact <问题>` | 变更影响分析 |
| `debug <问题>` | 根因分析 |
| `verify <问题>` | 可信度验证 |
| `self-critique <问题>` | 系统自我批评 |
| `patch <描述>` | 生成遵循约定的代码 |
| `map` | 查看认知流水线 |
| `health` | 架构健康度检查 |
| `summary` | 调查摘要 |
| `history` | 查询历史 |
| `export <文件.md>` | 导出报告 |
| `help` | 帮助 |

## Web API 端点

| 端点 | 说明 |
|---|---|
| `POST /api/load` | 加载仓库 |
| `POST /api/query` | 查询 |
| `POST /api/followup` | 追问 |
| `GET /api/session` | 会话状态 |
| `GET /api/evidence/:id` | 节点证据详情 |
| `POST /api/verify` | 可信度验证 |
| `POST /api/self-critique` | 自我批评 |
| `POST /api/patch` | 补丁生成 |
| `POST /api/present` | 渐进呈现 |
| `GET /api/observability/map` | 子系统地图 |
| `GET /api/observability/health` | 架构健康度 |

## 核心原则

- **确定性**：相同代码库 + 相同查询 = 永远相同结果
- **接地**：每条声明都有源码证据支撑
- **溯源可追溯**：每条解释可追溯到具体文件和符号
- **置信度校准**：不确定性显式表达，不隐藏
- **可回放**：结果可跨运行比较以进行回归测试
- **自我验证**：系统先评估自己的推理质量再输出

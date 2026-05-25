# CLI 使用指南

## 启动

```bash
dotnet run                          # 交互 REPL
dotnet run -- --web                 # Web API 模式 (http://localhost:5290)
dotnet run -- --scan <path>         # 传统全流水线扫描模式
dotnet run -- --load <path>         # 加载仓库并进入 REPL
```

## 全部命令

### 仓库操作

| 命令 | 说明 |
|---|---|
| `load <路径>` | 加载 .NET 解决方案或项目（首次全量扫描，后续缓秒加载) |
| `reload` | 强制重新扫描（跳过缓存） |
| `cache` | 显示缓存状态 |
| `clear` | 清除会话上下文 |
| `stats` | 显示仓库统计 |
| `health` | 检查架构健康度 |
| `map` | 显示认知流水线 |

### 查询

| 命令 | 说明 |
|---|---|
| `ask <问题>` | 自然语言查询（自动路由到最佳引擎） |
| `followup <问题>` | 追问上一个问题（保持上下文） |
| `arch <问题>` | 强制架构探索 |
| `impact <问题>` | 强制变更影响分析 |
| `capability <问题>` | 强制业务能力映射 |
| `debug <问题>` | 强制根因分析 |

### 验证与改进

| 命令 | 说明 |
|---|---|
| `verify <问题>` | 执行可信度验证（5维度检验） |
| `self-critique <问题>` | 生成系统自我批评 |
| `patch <描述>` | 生成遵循仓库约定的代码建议 |

### 会话管理

| 命令 | 说明 |
|---|---|
| `summary` | 显示调查摘要 |
| `history` | 显示查询历史 |
| `export <文件.md>` | 导出调查报告 |
| `help` | 显示帮助 |
| `exit` / `quit` | 退出 |

## 查询路由

问题自动路由到正确的引擎：

| 模式（中文） | 模式（英文） | 路由到 |
|---|---|---|
| "解释架构" "系统结构" "分层" "模块" | "architecture" "structure" "subsystem" | ArchitectureExplorer |
| "改动影响" "谁依赖" "会不会影响" | "what breaks" "who depends" "impact" | ChangeImpactAnalyzer |
| "在哪里实现" "谁处理" "哪个服务" | "where is" "how does" "implemented" | BusinessCapabilityMapper |
| "为什么失败" "错误" "超时" "重试" | "why does" "debug" "fail" "error" | GroundedRootCauseExplorer |

无法匹配时，系统在图里搜索匹配的类名/方法名，找到则路由到影响分析。

## 置信度级别

```
确定     [██████████] 100%  语义证据 + 符号绑定
高       [████████░░]  85%  图证据 + 源文件
中       [██████░░░░]  60%  部分证据
低       [████░░░░░░]  40%  有限证据
推测     [██░░░░░░░░]  20%  推断，非直接接地
无证据   [░░░░░░░░░░]   0%  无支撑
```

## 可信度裁定

```
StronglyGrounded          高度可信 — 证据充分，分析完整
PartiallyVerified         部分验证 — 存在不确定性
RuntimeIncomplete         运行时分析不完整
CompetingHypothesesPresent 存在竞争假设
LimitedEvidence           证据有限
RequiresFurtherInvestigation 需要更多调查
```

## 示例工作流

```
cognition> load D:\Projects\MyApp
cognition> ask "解释系统架构"
cognition> impact "改动 PaymentService 有什么影响"
cognition> verify "改动 PaymentService 有什么影响"
cognition> self-critique "为什么重试失败"
cognition> patch "在 PaymentService 加异常日志"
cognition> map
cognition> health
cognition> summary
cognition> export 调查.md
```

## 缓存

- 首次加载全量扫描（30-60秒），自动保存缓存到 `%LOCALAPPDATA%\ContextEngine\cache\`
- 后续加载同一路径从缓存恢复（<1秒）
- `reload` 强制重新扫描
- `cache` 查看缓存状态
- WebUI 左侧栏有 "🗑 清除缓存" 按钮

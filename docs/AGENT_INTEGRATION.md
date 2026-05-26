# ContextEngine — 外部 Agent 集成指南

> ContextEngine 是一个**纯粹的 Repository Cognition Server**。它不调用任何 LLM API，
> 而是通过 MCP 协议或 HTTP API 向外部 AI 代码代理提供**接地（grounded）的仓库认知能力**。
>
> 外部 Agent 拥有 LLM 调用权，ContextEngine 提供确定性的代码图查询、语义搜索、
> 认知推理和代码修改构建块。

---

## 目录

1. [快速开始](#快速开始)
2. [集成模式一：MCP 协议（推荐）](#集成模式一mcp-协议推荐)
3. [集成模式二：Web API](#集成模式二web-api)
4. [工具目录（24 个工具）](#工具目录24-个工具)
5. [典型工作流](#典型工作流)
6. [集成示例代码](#集成示例代码)

---

## 快速开始

### 构建

```bash
cd D:\我的框架\ContextEngine
dotnet build    # 0 errors
```

### 对接 AI Agent — 三种方式概览

| 方式 | 适用场景 | 配置复杂度 |
|------|---------|-----------|
| MCP 协议（推荐） | 支持 MCP 的 Agent（Claude Code、Cursor、Continue 等） | 一行配置 |
| Web API | 自定义 Agent、Web 前端、不支持 MCP 的工具 | 启动 HTTP 服务 |
| 子进程直连 | Python/Node.js 脚本、CI/CD 流水线、自动化测试 | 写 ~50 行代码 |

---

### 方式 A：MCP 协议 — 一行配置接入

**项目已包含 `.mcp.json`**，Agent 可以自动发现并启动 ContextEngine。

#### Claude Code

直接在当前仓库中使用——Claude Code 会自动加载 `.mcp.json`：

```
用户直接在 Claude Code 中提问，ContextEngine 的 24 个工具会自动出现在工具列表中。
```

对其他仓库，在 Claude Code 的 MCP 配置中添加（`claude mcp add` 或编辑配置文件）：

```json
{
  "mcpServers": {
    "contextengine": {
      "command": "dotnet",
      "args": ["run", "--", "--mcp", "--repo", "D:\\Projects\\目标仓库"],
      "cwd": "D:\\我的框架\\ContextEngine"
    }
  }
}
```

#### VS Code / Cursor（使用 MCP 扩展）

在 `.vscode/mcp.json` 或 Cursor 的 MCP 设置中：

```json
{
  "servers": {
    "contextengine": {
      "command": "dotnet",
      "args": ["run", "--", "--mcp", "--repo", "${workspaceFolder}"],
      "cwd": "D:\\我的框架\\ContextEngine"
    }
  }
}
```

#### Continue（VS Code / JetBrains）

在 `~/.continue/config.json` 的 `mcpServers` 中：

```json
{
  "mcpServers": [
    {
      "name": "contextengine",
      "command": "dotnet",
      "args": ["run", "--", "--mcp", "--repo", "${workspaceFolder}"],
      "cwd": "D:\\我的框架\\ContextEngine"
    }
  ]
}
```

#### 任何支持 MCP 的 Agent

标准 MCP 配置格式都一样——三个字段：

| 字段 | 值 | 说明 |
|------|----|------|
| `command` | `dotnet` | .NET 运行时 |
| `args` | `["run", "--", "--mcp", "--repo", "<仓库路径>"]` | 指向要分析的目标仓库 |
| `cwd` | `D:\\我的框架\\ContextEngine` | ContextEngine 项目根目录 |

> 替换 `<仓库路径>` 为实际要分析的 .NET 解决方案路径。

---

### 方式 B：Web API — 启动 HTTP 服务

```bash
cd D:\我的框架\ContextEngine
dotnet run -- --web
# 监听 http://localhost:5290
```

然后通过 API 加载仓库：
```bash
curl -X POST http://localhost:5290/api/load \
  -H "Content-Type: application/json" \
  -d '{"path":"D:/Projects/MySolution"}'
```

Agent 通过 HTTP 调用 `/api/agent` 等端点（详见下方 Web API 章节）。

---

### 方式 C：Python/Node.js 子进程直连

适用于自定义 Agent、自动化脚本、CI/CD 流水线。完整客户端代码见下方[集成示例代码](#集成示例代码)章节。

---

## 集成模式一：MCP 协议（推荐）

### 协议概述

ContextEngine 实现了 [Model Context Protocol](https://spec.modelcontextprotocol.io/) 的 JSON-RPC 2.0 子集，通过 stdio 传输。

**线格式**：每条消息由 `Content-Length: N\r\n\r\n` 头 + JSON body 组成。

```
Content-Length: 78\r\n
\r\n
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"0.1.0"}}
```

**消息流向**：Agent → stdin → ContextEngine → stdout → Agent

**错误信息**输出到 stderr（不影响协议），包括加载进度和异常。

### 启动与初始化

Agent 启动 ContextEngine 作为子进程：

```
dotnet run -- --mcp --repo <仓库路径>
```

服务器启动后自动加载仓库、构建代码图。加载完成后在 stderr 输出 `ContextEngine MCP server started`。

Agent 发送的第一条消息必须是 `initialize`：

```json
// → 发送
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": { "protocolVersion": "0.1.0" }
}

// ← 响应
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "0.1.0",
    "capabilities": { "tools": {} },
    "serverInfo": { "name": "ContextEngine", "version": "1.0.0" }
  }
}
```

### 发现工具

```json
// → 发送
{ "jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {} }

// ← 响应（省略部分工具）
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "tools": [
      {
        "name": "ce_search_nodes",
        "description": "Search graph nodes by name, class, namespace, or kind...",
        "inputSchema": {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search term..." },
            "kind": { "type": "string", "description": "Optional node kind filter..." },
            "limit": { "type": "number", "description": "Max results (default 20)" }
          },
          "required": ["query"]
        }
      }
      // ... 共 24 个工具
    ]
  }
}
```

### 调用工具

```json
// → 发送
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "ce_search_nodes",
    "arguments": { "query": "PaymentService", "limit": 5 }
  }
}

// ← 响应
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"total\":2,\"results\":[{\"id\":\"method:...\",\"label\":\"PaymentService.Process\",...}]}"
      }
    ]
  }
}
```

> 注意：工具返回的 `content[0].text` 是 **JSON 字符串**，需要二次解析。

---

## 集成模式二：Web API

**Base URL**: `http://localhost:5290`

### 仓库管理

| 方法 | 端点 | 说明 |
|------|------|------|
| POST | `/api/load` | 加载仓库 `{"path":"...", "forceReload":false}` |
| GET | `/api/load/status` | 加载进度（含 `complete`, `result`） |
| POST | `/api/reload` | 重新扫描当前仓库 |
| GET | `/api/session` | 当前会话状态（是否加载、节点数、查询历史） |

### 统一 Agent 端点

```
POST /api/agent
{"message": "解释支付架构"}
```

返回认知分析结果，包含 `explanations`、`citations`、`confidence` 等。

### 认知端点

| 端点 | 说明 |
|------|------|
| `POST /api/cognition/ask` | 提问 `{"question":"..."}` |
| `POST /api/cognition/verify` | 验证可信度 `{"question":"..."}` |
| `POST /api/cognition/self-critique` | 自我批评 `{"question":"..."}` |
| `POST /api/cognition/epistemic-boundary` | 认知边界分析 `{"question":"..."}` |
| `POST /api/cognition/patch` | 生成补丁 `{"request":"..."}` |

### 可观测性

| 端点 | 说明 |
|------|------|
| `GET /api/observability/map` | 子系统分层图 |
| `GET /api/observability/health` | 架构健康检查 |
| `GET /api/observability/pipeline` | 认知管道图（文本） |

### 证据

| 端点 | 说明 |
|------|------|
| `GET /api/evidence/{nodeId}` | 节点详情（调用者、被调用者、接地信息） |

---

## 工具目录（24 个工具）

### 一、图查询（Graph Query）— 10 个工具

#### `ce_search_nodes` — 搜索节点
```
参数: query(必填), kind(可选: method/entity/table), limit(可选, 默认20)
返回: { total, results: [{ id, label, kind, className, sourceFile }] }
```
```json
// 请求
{"query": "PaymentService", "limit": 5}
// 响应
{"total": 3, "results": [
  {"id": "method:MyApp.csproj::MyApp.Services.PaymentService.Process(Order)",
   "label": "PaymentService.Process(Order)", "kind": "method",
   "className": "PaymentService", "sourceFile": "Services/PaymentService.cs"}
]}
```

#### `ce_get_node` — 获取节点详情
```
参数: methodId(必填) — 稳定的方法 ID，格式: Project.Class.Method(params)
返回: { id, label, kind, sourceFile, symbolHandle, className, namespace, 
        groundingKind, truthType, confidence, callerCount, calleeCount }
```

#### `ce_get_callers` — 获取调用者（上游依赖）
```
参数: methodId(必填)
返回: [{ id, label, kind, sourceFile }]  — 谁调用了这个方法
```

#### `ce_get_callees` — 获取被调用者（下游依赖）
```
参数: methodId(必填)
返回: [{ id, label, kind, sourceFile }]  — 这个方法调用了谁
```

#### `ce_get_call_chain` — 展开调用链
```
参数: methodId(必填), depth(可选, 默认3)
返回: [{ paths: [{ nodeIds, labels, edgeKinds, depth, summary }] }]
      多个分支时返回多条路径
```

#### `ce_get_edges` — 获取边关系
```
参数: methodId(必填), direction(可选: in/out/both, 默认both)
返回: { incoming: [{ from, to, kind }], outgoing: [{ from, to, kind }] }
边类型: call, spring:implements, nh:entity-access, spring:property-ref 等
```

#### `ce_find_semantic_path` — 查找语义路径
```
参数: fromId(必填), toId(必填), maxDepth(可选, 默认15)
返回: { paths: [{ nodeIds, labels, edgeKinds, depth, summary }], total }
在调用、Spring、NHibernate 三种边类型中查找多跳路径
```

#### `ce_find_entry_points` — 追溯 HTTP 入口点
```
参数: methodId(必填)
返回: { total, entryPoints: [...] }
从方法向上追溯到 ASP.NET Controller/Route
```

#### `ce_list_entry_points` — 列出所有入口点
```
参数: 无
返回: { total, entryPoints: [{ id, label, kind, route, httpMethod }] }
```

#### `ce_get_stats` — 图统计信息
```
参数: 无
返回: { totalNodes, totalEntryPoints, byKind: { method: N, external: N, ... } }
```

---

### 二、认知推理（Cognition）— 5 个工具

#### `ce_ask` — 自然语言提问
```
参数: question(必填) — 支持中英文
返回: {
  resultId, query, resultType, overallConfidence, evidenceCount,
  explanations: [{ text, claim, confidenceLevel, supportingNodeIds }],
  citations: [{ sourceNodeId, sourceNodeLabel, sourceFile, confidenceLevel }]
}
```
**自动路由**：问题被分类后路由到最合适的认知引擎：
- 架构探索 → ArchitectureExplorer
- 影响分析 → ChangeImpactAnalyzer
- 能力映射 → BusinessCapabilityMapper
- 根因分析 → GroundedRootCauseExplorer

#### `ce_verify` — 验证答案可信度
```
参数: question(必填) — 与 ce_ask 相同的问题
返回: {
  verdict, verdictDisplay, trustScore, summary,
  grounding: { score, totalCitations, citationsWithFiles, issues },
  coverage: { score, analysisCompleteness, unresolvedDispatchCount },
  calibration: { score, isOverConfident, claimedConfidence },
  hypotheses: { score, competingCount, alternatives },
  utility: { score, isActionable, hasEngineeringGuidance }
}
```
在 `ce_ask` 之后调用，用 5 个验证器检查答案可靠性。

#### `ce_self_critique` — 自我批评
```
参数: question(必填)
返回: {
  critiqueId, honestyStatement, isHighQuality,
  weaknesses, unknowns, confidenceReductions,
  evaluation: { groundingScore, epistemicHonestyScore, completenessScore,
                precisionScore, contradictionScore, recommendedAction }
}
```
找出答案的弱点、未知区域和需要降低信心的理由。

#### `ce_epistemic_boundary` — 认知边界分析
```
参数: question(必填)
返回: {
  canPresentAsDefinitiveConclusion,
  groundedPresentCount, groundedAbsentCount, unresolvedCount,
  incompleteCount, notSearchedCount,
  confidence: { entityResolution, impactAnalysis, runtimeCompleteness },
  annotations: [{ subject, evidenceState, explanation }],
  coverageGaps: [{ gapType, description, suggestedAction, severity }]
}
```
分析哪些证据已确认、哪些缺失、哪些未搜索——判断是否可以作为确定结论。

#### `ce_patch` — 生成接地补丁
```
参数: request(必填) — "给 XX 添加 YY 功能"
返回: {
  explanation, plan: { strategy, modificationPoints, impactedServices },
  validation: { isSafe, overallRisk, riskFactors },
  patches: [{ patchId, targetFile, description, generatedCode, conventionsApplied }]
}
```
解释修改方案 → 生成修改计划 → 验证影响 → 输出接地代码。

---

### 三、代码修改构建块（Code-Fix Building Blocks）— 6 个工具

这 6 个工具是为外部 AI Agent 设计的**原子操作**，Agent 可以自由组合它们来实现任意代码修改工作流。

#### `ce_locate_symbol` — 定位目标方法
```
参数: query(必填), targetMethodName(可选), targetFilePath(可选)
返回: { total, symbols: [{
  nodeId, methodName, className, namespace, sourceFilePath,
  methodStartLine, methodEndLine, methodBody, fullSignature,
  isPrivate, isPublicApi, parameterTypes,
  callees: [{ methodName, fullSignature }],
  callers: [{ methodName, fullSignature }]
}] }
```
用自然语言描述定位代码中的方法/符号。支持语义搜索 + 关键词回退。

#### `ce_extract_context` — 提取方法上下文
```
参数: nodeId(必填) — 由 ce_locate_symbol 返回
返回: {
  context,            // 完整的格式化上下文（供 LLM 使用）
  targetMethodBody, targetSignature, targetClassName,
  targetNamespace, targetFileName,
  usingDirectives,    // using 声明列表
  interfaceMethods,   // 接口约定
  relatedMethods      // 相关方法及摘要
}
```
提取 LLM 生成代码所需的最小上下文：using、接口、相关方法、完整方法体。

#### `ce_validate_patch` — 验证补丁
```
参数: filePath(必填), originalCode(必填), modifiedCode(必填),
      lineStart(可选), lineEnd(可选), changeDescription(可选),
      kind(可选: ModifyExisting/CreateNewFile), nodeId(可选)
返回: { isValid, violations: ["签名变更", "公共API破坏", ...] }
```
在应用补丁前检查：签名保留、公共 API 安全、配置/DI 防篡改。

#### `ce_apply_patch` — 应用补丁到磁盘
```
参数: filePath(必填), originalCode(必填), modifiedCode(必填),
      lineStart(可选), lineEnd(可选), changeDescription(可选),
      kind(可选: ModifyExisting/CreateNewFile), projectDir(可选)
返回: { applied: true, filePath, patchId, kind }
```
ModifyExisting：替换源文件中的行范围。CreateNewFile：创建新的 .cs 文件。

#### `ce_revert_patch` — 撤销补丁
```
参数: filePath(必填), originalCode(必填), modifiedCode(必填),
      lineStart(可选), lineEnd(可选), changeDescription(可选),
      kind(可选: ModifyExisting/CreateNewFile)
返回: { reverted: true, filePath, kind }
```
ModifyExisting：恢复原始行。CreateNewFile：删除创建的文件。

#### `ce_build` — 编译验证
```
参数: projectPath(必填) — .csproj 或解决方案目录路径
返回: {
  success, exitCode, durationMs,
  errors: [{ code, message, filePath, line, column, isError, context }],
  warnings: ["..."]
}
```
应用补丁后验证编译是否通过。

---

### 四、影响分析（Impact Analysis）— 3 个工具

#### `ce_find_impact` — 双向影响分析
```
参数: methodId(必填)
返回: { upstream: [callers], downstream: [data access], ... }
分析修改某方法会破坏什么。
```

#### `ce_find_table_impact` — 数据库表影响分析
```
参数: tableName(必填)
返回: { entryPoints: [...] }
Table → Entity → Repository → Service → Controller → Route
```

#### `ce_find_routes_to_table` — 路由到表的追溯
```
参数: tableName(必填)
返回: { paths: [...] }
Route → Controller → Service → Repository → Entity → Table
```

---

## 典型工作流

### 工作流 1：代码修改（Code-Fix Pipeline）

外部 Agent 执行代码修改的推荐流程：

```
┌─────────────────────────────────────────────────────────┐
│  Agent 负责: 理解需求、生成代码、决策是否应用            │
│  ContextEngine 负责: 定位、上下文提取、验证、写入、编译  │
└─────────────────────────────────────────────────────────┘

第 1 步: ce_locate_symbol
  → 用自然语言定位要修改的方法
  → 获取 nodeId, 方法体, 签名, sourceFilePath

第 2 步: ce_extract_context
  → 提取 using 声明、接口约定、相关方法
  → 将 context 字段传给 LLM 作为代码生成提示

第 3 步: [Agent 调用 LLM 生成修改后的代码]
  → 这是 Agent 自己做的事，ContextEngine 不参与

第 4 步: ce_validate_patch
  → 传入原始代码和修改后代码
  → 检查签名是否被保留、公共 API 是否安全

第 5 步: ce_apply_patch
  → 将验证通过的补丁写入磁盘

第 6 步: ce_build
  → 编译项目验证修改是否正确

第 7 步（如需要）: ce_revert_patch
  → 如果编译失败或需要回滚，撤销修改
```

**MCP 消息序列示例**：

```
Step 1: tools/call ce_locate_symbol {"query":"支付重试逻辑"}
Step 2: tools/call ce_extract_context {"nodeId":"method:..."}
Step 3: [Agent internal LLM call — ContextEngine not involved]
Step 4: tools/call ce_validate_patch {"filePath":"...","originalCode":"...","modifiedCode":"..."}
Step 5: tools/call ce_apply_patch   {"filePath":"...","originalCode":"...","modifiedCode":"..."}
Step 6: tools/call ce_build         {"projectPath":"D:/Projects/MyApp"}
```

### 工作流 2：架构理解

```
ce_search_nodes → ce_get_node → ce_get_callers → ce_get_callees → ce_ask
```

1. `ce_search_nodes` 找到感兴趣的节点
2. `ce_get_node` 查看详情
3. `ce_get_callers` / `ce_get_callees` 理解依赖关系
4. `ce_ask` 获得架构级别的解释

### 工作流 3：影响评估

```
ce_find_impact → ce_verify → ce_epistemic_boundary
```

1. `ce_find_impact` 分析修改影响范围
2. `ce_verify` 验证分析可信度
3. `ce_epistemic_boundary` 检查是否有遗漏

### 工作流 4：可信度验证

```
ce_ask → ce_verify → ce_self_critique → ce_epistemic_boundary
```

逐步升级的验证链：基础回答 → 5 维验证 → 自我批评 → 认知边界

---

## 集成示例代码

### Python — MCP 客户端

```python
"""Minimal MCP client for ContextEngine."""
import subprocess, json, select

class ContextEngineClient:
    def __init__(self, repo_path: str):
        self.proc = subprocess.Popen(
            ["dotnet", "run", "--", "--mcp", "--repo", repo_path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=False,
        )
        self._wait_ready()
        self._initialize()

    def _wait_ready(self, timeout=120):
        """Wait for 'ContextEngine MCP server started' on stderr."""
        import time
        deadline = time.time() + timeout
        output = b""
        while time.time() < deadline:
            ready, _, _ = select.select([self.proc.stderr], [], [], 1.0)
            if ready:
                chunk = self.proc.stderr.read1(4096)
                if chunk:
                    output += chunk
                    if b"ContextEngine MCP server started" in output:
                        return
            if self.proc.poll() is not None:
                raise RuntimeError(f"Process exited: {self.proc.returncode}")
        raise TimeoutError("Server did not start in time")

    def _send(self, msg: dict):
        body = json.dumps(msg, ensure_ascii=False)
        wire = f"Content-Length: {len(body.encode('utf-8'))}\r\n\r\n{body}"
        self.proc.stdin.write(wire.encode())
        self.proc.stdin.flush()

    def _recv(self, timeout=30):
        header = b""
        while b"\r\n\r\n" not in header:
            ready, _, _ = select.select([self.proc.stdout], [], [], timeout)
            if not ready:
                return None
            chunk = self.proc.stdout.read1(4096)
            if not chunk:
                return None
            header += chunk
        header_str = header.split(b"\r\n\r\n")[0].decode()
        content_length = int(header_str.split(": ")[1])
        body = header.split(b"\r\n\r\n", 1)[1] if b"\r\n\r\n" in header else b""
        while len(body) < content_length:
            chunk = self.proc.stdout.read1(content_length - len(body))
            if not chunk:
                break
            body += chunk
        return json.loads(body[:content_length].decode())

    def _initialize(self):
        self._send({"jsonrpc": "2.0", "id": 0, "method": "initialize",
                      "params": {"protocolVersion": "0.1.0"}})
        self._recv()

    def call_tool(self, name: str, arguments: dict) -> dict:
        self._send({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                     "params": {"name": name, "arguments": arguments}})
        resp = self._recv()
        text = resp["result"]["content"][0]["text"]
        return json.loads(text)

    def close(self):
        self.proc.kill()
        self.proc.wait()

# 使用示例
if __name__ == "__main__":
    ce = ContextEngineClient("D:\\Projects\\MySolution")

    # 获取统计信息
    stats = ce.call_tool("ce_get_stats", {})
    print(f"节点数: {stats['totalNodes']}")

    # 搜索方法
    result = ce.call_tool("ce_search_nodes", {"query": "Payment", "limit": 5})
    for r in result["results"]:
        print(f"  {r['label']} ({r['kind']})")

    # 认知查询
    answer = ce.call_tool("ce_ask", {"question": "解释支付架构"})
    for exp in answer["explanations"]:
        print(f"  [{exp['confidenceLevel']}] {exp['text']}")

    ce.close()
```

### TypeScript — MCP 客户端（Node.js）

```typescript
import { spawn } from "child_process";
import { createInterface } from "readline";

class ContextEngineClient {
  private proc: ReturnType<typeof spawn>;
  private buffer = "";
  private resolvers: Map<number, (value: any) => void> = new Map();
  private nextId = 1;

  constructor(repoPath: string) {
    this.proc = spawn("dotnet", ["run", "--", "--mcp", "--repo", repoPath], {
      stdio: ["pipe", "pipe", "pipe"],
    });
    this.waitReady().then(() => this.init());
  }

  private async waitReady(): Promise<void> {
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("Startup timeout")), 120_000);
      this.proc.stderr!.on("data", (chunk: Buffer) => {
        if (chunk.toString().includes("ContextEngine MCP server started")) {
          clearTimeout(timeout);
          resolve();
        }
      });
      this.proc.on("exit", (code) => reject(new Error(`Exit ${code}`)));
    });
  }

  private sendMessage(msg: object): void {
    const body = JSON.stringify(msg);
    const wire = `Content-Length: ${Buffer.byteLength(body)}\r\n\r\n${body}`;
    this.proc.stdin!.write(wire);
  }

  private init(): void {
    const rl = createInterface({ input: this.proc.stdout! });
    rl.on("line", (line) => {
      if (line.startsWith("Content-Length:")) {
        // Parse MCP wire format — simplified, production code
        // should accumulate bytes properly
      }
    });
    this.sendMessage({
      jsonrpc: "2.0", id: 0, method: "initialize",
      params: { protocolVersion: "0.1.0" },
    });
  }

  // ... implement full MCP wire read/write

  close(): void {
    this.proc.kill();
  }
}
```

### HTTP API 调用示例

```bash
# 加载仓库
curl -X POST http://localhost:5290/api/load \
  -H "Content-Type: application/json" \
  -d '{"path":"D:/Projects/MySolution"}'

# 等待加载完成
curl http://localhost:5290/api/load/status

# 认知查询
curl -X POST http://localhost:5290/api/agent \
  -H "Content-Type: application/json" \
  -d '{"message":"解释支付架构的依赖关系"}'

# 验证可信度
curl -X POST http://localhost:5290/api/cognition/verify \
  -H "Content-Type: application/json" \
  -d '{"question":"RetryPolicy 的改动会影响哪些服务"}'

# 获取节点证据
curl http://localhost:5290/api/evidence/method:MyApp.csproj::MyApp.Services.PaymentService.Process
```

---

## Agent 与 ContextEngine 的职责边界

```
┌──────────────────────────────────────────────────────────┐
│                     外部 AI Agent                         │
│  职责:                                                    │
│  • 理解用户意图                                           │
│  • 调用 LLM 生成代码                                      │
│  • 决策是否应用修改                                       │
│  • 多轮对话管理                                           │
│  • 跨仓库知识整合                                         │
└──────────────┬───────────────────────────────────────────┘
               │  MCP (stdin/stdout) 或 HTTP
               │  24 个认知工具
               ▼
┌──────────────────────────────────────────────────────────┐
│                    ContextEngine                          │
│  职责:                                                    │
│  • 扫描 .NET 解决方案，构建代码图                          │
│  • 提供确定性图查询（调用者、被调用者、路径）               │
│  • 接地认知推理（架构探索、影响分析、根因分析）             │
│  • 认知验证（5 维可信度评估）                              │
│  • 代码修改构建块（定位、上下文提取、验证、写入、编译）     │
│  • 绝不调用 LLM API                                       │
└──────────────────────────────────────────────────────────┘
```

## 常见问题

**Q: ContextEngine 自己能修复 bug 吗？**
A: 不能。ContextEngine 提供代码定位、上下文提取、补丁验证/应用/编译等构建块。
   外部 Agent 负责调用 LLM 生成修复代码，ContextEngine 负责接地执行。

**Q: 支持哪些语言？**
A: 当前仅支持 C# (.NET) 项目。图构建依赖 Roslyn 语义分析。

**Q: MCP 和 HTTP 模式可以同时使用吗？**
A: 不可以。每次启动只能选择一种模式。MCP 模式是单会话的（通过 stdio），
   HTTP 模式可以服务多个客户端。

**Q: 冷启动需要多长时间？**
A: 取决于仓库大小。ContextEngine 自身（218 个 .cs 文件）约 15-20 秒。
   百万行级别项目可能需要 1-2 分钟。后续启动使用缓存加速。

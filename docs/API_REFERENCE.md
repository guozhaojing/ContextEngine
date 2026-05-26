# API Reference

Base URL: `http://localhost:5290`

## Repository

### POST /api/load
Load a .NET solution/project.

```json
{ "path": "D:\\Projects\\MySolution", "forceReload": false }
```
Response:
```json
{ "success": true, "repositoryName": "MySolution", "nodeCount": 2500, "edgeCount": 17800, "projectCount": 25, "fromCache": false }
```

### POST /api/reload
Force rescan current repository. No body.

### GET /api/session
Current session state: `{ "isLoaded": true, "repositoryName": "...", "nodeCount": 2500, ... }`

### GET /api/history
Saved repository paths: `[{ "path": "...", "name": "...", "nodeCount": 2500, "lastUsed": "..." }]`

### DELETE /api/history
Remove history entry: `{ "path": "D:\\Projects\\MySolution" }`

### POST /api/cache/clear
Clear cache for path: `{ "path": "D:\\Projects\\MySolution" }`

## Agent (Unified)

### POST /api/agent
Unified cognition endpoint. Delegates to the same backend as MCP tools.

```json
{ "message": "解释支付架构的依赖关系" }
```

Response:
```json
{
  "resultId": "arch-...",
  "query": "解释支付架构的依赖关系",
  "resultType": "ArchitectureExplanation",
  "overallConfidence": "Strong",
  "evidenceCount": 5,
  "explanations": [{ "text": "...", "claim": "...", "confidenceLevel": "Strong", "supportingNodeIds": [...] }],
  "citations": [{ "sourceNodeId": "...", "sourceNodeLabel": "...", "sourceFile": "...", "confidenceLevel": "..." }]
}
```

For code modification, use the MCP code-fix building blocks (ce_locate_symbol, ce_extract_context, ce_validate_patch, ce_apply_patch, ce_build). See `AGENT_INTEGRATION.md`.

## Evidence

### GET /api/evidence/{nodeId}
Node detail: `{ "nodeId": "...", "label": "...", "sourceFile": "...", "callers": [...], "callees": [...], "confidence": 0.95 }`

## Observability

### GET /api/observability/map
Subsystem list: `[{ "name": "Core.Scanning", "layer": "数据采集", "purpose": "...", "dependsOn": [...], "usedBy": [...] }]`

### GET /api/observability/health
Complexity report: `{ "isHealthy": true, "overallRisk": "Low", "findings": [...] }`

### GET /api/observability/pipeline
Cognition pipeline diagram (text/plain).

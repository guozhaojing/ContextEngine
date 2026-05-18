# Query Understanding (Phase 5B)

## Overview

Rule-based, deterministic query understanding system. No LLM. 10 files under `Core/QueryUnderstanding/`.

## Intent Classification

`QueryIntentClassifier.Classify(query)` → `QueryIntent` enum:

| Intent | Trigger Keywords (Chinese + English) |
|--------|--------------------------------------|
| `FlowAnalysis` | flow, 流程, call, 调用, chain, 链, path, 路径, trace, 追踪, pipeline, 管线 |
| `ImpactAnalysis` | impact, 影响, affect, 波及, change, 变更, modify, 修改, depend, 依赖, risk, 风险 |
| `EntityLookup` | entity, 实体, table, 表, data, 数据, EQA, equip, reagent, lab, repository, dao |
| `RouteLookup` | route, 路由, api, 接口, endpoint, 端点, controller, action |
| `ValidationLookup` | validate, 校验, verify, 验证, check, 检查, rule, 规则, business, 业务 |

Strategy: tokenize (PascalCase + Chinese 2-grams), score by keyword counts, tiebreak by priority: Entity > Route > Flow > Impact > Validation.

## VocabularyBuilder

Extracts project vocabulary from actual scanned artifacts:

| Source | → Vocabulary Entry Kind |
|--------|------------------------|
| `nh-entity-access` facts → `entityClass` field | `entity` |
| `nh-entity-access` facts → `table` field | `table` |
| Entity external node ID suffix (`ext::nh:entity::NS.Class::Table`) | `table` |
| `http-route` facts → `route` + `controller` fields | `route`, `class` |
| `GraphNode.Kind == Method` → `ClassName`, `MethodName` | `class`, `method` |
| CodeUnit Namespace components | `normalized` |

Output: `ProjectVocabulary` containing 5 entry lists + normalized terms.

## AliasGraph

Bidirectional entity alias graph. Nodes represent Table/Entity/Route/Repository/Controller. Edges represent naming equivalence.

Construction:
1. Table ↔ Entity: from `nh-entity-access` facts linking entityClass to table
2. Entity ↔ Repository: from node Attributes["entity"] linking entity to the class accessing it
3. Controller ↔ Route: from `http-route` facts

Query: `FindByName(name)`, `ExpandToAliases(id, maxDepth=2)` (BFS), `FromVocabulary(ProjectVocabulary)`.

## Normalization

### PascalCase → tokens
```
GetListBySpecialtyID → [get, list, specialty, id]
EQA_EquipGRelation  → [eqa, equip, group, relation]
```

### Route path → tokens
```
/api/reagent-flow/{id} → [api, reagent, flow, id]
```

### Abbreviation expansion (23 entries)
`ID→id, EQA→eqa, GR→group, REL→relation, SVC→service, DAO→dao, ...`

## Query Expansion

5 strategies, each producing `ExpansionCandidate { Term, Source, Score }`:

| Strategy | Score | Description |
|----------|-------|-------------|
| Synonym | 0.85–0.90 | Chinese↔English mapping, bidirectional lookup |
| Vocabulary exact | 0.95–1.0 | Exact match on normalized name or token |
| Fuzzy prefix | 0.7 | Token starts with or is prefixed by query |
| Fuzzy contains | 0.5 | Substring match |
| Suffix template | 0.6 | Append "Service"/"Dao"/"Repository" to token |
| Alias graph | 0.8 | BFS expansion via entity alias graph |
| Compound | 0.85+ | Multi-token match against vocabulary entries |

## Query Rewriting

Input: `"试剂流程"` (reagent flow)

Output: `RewrittenQuery`
```
ExpandedQuery: "reagent EQA_Reagent flow entity access repository"
Tokens: [reagent, eqa_reagent, flow, entity, access, repository]
Intent: EntityLookup
Sources: [expanded:synonym(0.90), expanded:vocab:entity(1.00), intent:EntityLookup, ...]
```

Strategy:
1. Collect top-3 candidates per token (score ≥ 0.6)
2. Collect top-3 compound expansions (score ≥ 0.7)
3. Append intent-specific keywords
4. Generate lowercase + normalized token variants

## Explanation

`QueryExplanation` output for each query:
- `MatchedEntities`: which entity classes matched
- `MatchedTables`: which database tables matched
- `MatchedRoutes`: which API routes matched
- `MatchedKeywords`: which vocabulary/keywords matched
- `TotalConfidence`: average confidence across all matches
- Each match includes: `MatchedTerm, Category, Source, Confidence, OriginalName, FilePath`

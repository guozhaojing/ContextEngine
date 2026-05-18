# Retrieval Pipeline

> **Status**: Designed, not yet implemented. This document specifies the planned retrieval architecture.

## Pipeline Stages

```
User Query
    │
    ▼
┌──────────────────────────┐
│ Query Understanding      │  Phase 5B (implemented)
│ - Intent classification  │
│ - Vocabulary expansion   │
│ - Query rewriting        │
└──────────┬───────────────┘
           │ rewritten query + tokens
           ▼
┌──────────────────────────┐
│ Chunking                 │  (designed)
│ - Method-level chunks    │
│ - Class-level chunks     │
│ - Cross-file chunks      │
│ - Overlap strategy       │
└──────────┬───────────────┘
           │ chunks
           ▼
┌──────────────────────────┐
│ Embedding                │  (designed)
│ - Dense vectors (BERT)   │
│ - Source code embedding  │
│ - Metadata embedding     │
└──────────┬───────────────┘
           │ vectors
           ▼
┌──────────────────────────┐
│ Hybrid Retrieval         │  (designed)
│ - Dense (cosine)         │
│ - Sparse (BM25)          │
│ - Graph-aware (EdgeIndex)│
│ - Weighted combination   │
└──────────┬───────────────┘
           │ ranked results
           ▼
┌──────────────────────────┐
│ Explanation              │  Phase 5B (implemented)
│ - Match trace            │
│ - Confidence scores      │
│ - Entity/Table/Route map │
└──────────────────────────┘
```

## Chunking Strategy

| Chunk Type | Granularity | Source |
|------------|-------------|--------|
| Method chunk | One method body | CodeUnit.Content |
| Class chunk | All methods of a class | Grouped CodeUnits |
| Fact chunk | Analyzer fact + data | GraphFact |
| Entity chunk | Entity → Table mapping | nh:entity-access facts + generic resolution |
| Route chunk | Route template + controller | http-route facts |

Overlap: method chunks include class-level context prefix. Entity chunks include related repository names.

## Embedding Model

- **Dense**: local embedding model (candidate: `all-MiniLM-L6-v2` or similar)
- **Code-aware**: source code tokenized with language-specific tokenizer
- **Metadata**: entity names, table names, route paths embedded separately for exact match

## Hybrid Retrieval

### Components

| Component | Weight | Role |
|-----------|--------|------|
| Dense retrieval | 0.4 | Semantic similarity (expanded query) |
| Sparse retrieval (BM25) | 0.3 | Exact token matching (vocabulary terms) |
| Graph-aware retrieval | 0.3 | Structural relevance (EdgeIndex callers/callees) |

### Ranking Factors

| Factor | Description |
|--------|-------------|
| Semantic similarity | Cosine distance between query embedding and chunk embedding |
| Vocabulary match | Number of project vocabulary tokens matched |
| Confidence | Edge/Fact confidence score (Exact > High > Medium > Low) |
| Graph centrality | Fan-in / fan-out of matched method nodes |
| EntryPointDistance | Distance from matched node to nearest entry point |
| Layer diversity | Number of layers (Call/Framework/Data) traversed |

## Benchmark Metrics

| Metric | Definition |
|--------|------------|
| Precision@K | Relevant results in top K / K |
| Recall@K | Relevant results in top K / total relevant |
| MRR | Mean Reciprocal Rank of first relevant result |
| Entity coverage | % of known entities represented in retrieval results |
| Table coverage | % of known tables reachable via retrieval paths |
| Route→Table paths | Number of complete Route→Entity→Table chains resolved |
| Generic resolution rate | % of repository methods with resolved entity type |

## Explainability

Every retrieval result includes:

| Field | Source |
|-------|--------|
| Matched entity class | `nh-entity-access` fact data |
| Matched table name | `nh-entity-access` fact data |
| Matched route template | `http-route` fact data |
| Resolution confidence | Edge attribute `confidence` |
| Resolution method | `generic:resolved` annotation or direct session API |
| Via class | Generic resolution chain origin |
| Source file | Original `.cs` file path for traceability |

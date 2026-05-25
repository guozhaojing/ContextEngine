# Roadmap

## Completed

- [x] Roslyn scanning + graph building + analysis pipeline
- [x] Semantic traversal + query understanding + generic resolution
- [x] Grounding enforcement: claim validation, hallucination blocking, citation constraint
- [x] Confidence propagation: 8 decay rules, deterministic BFS
- [x] Contradiction detection: 8 types, consistency validation
- [x] Self-validation: 5-dim scoring, 6 risk types, self-critique
- [x] Verification: 5-dim trustworthiness, 6 verdict levels
- [x] Cognition engines: ArchitectureExplorer, ChangeImpactAnalyzer, BusinessCapabilityMapper, GroundedRootCauseExplorer
- [x] Epistemic boundary: 6 evidence states, triple confidence
- [x] Progressive reasoning presentation: 5-layer progressive disclosure
- [x] Observability: system maps, architecture narration, complexity analysis
- [x] CLI REPL: 14 commands, query routing, repository cache
- [x] Web API + WebUI: REST endpoints, React chat workspace
- [x] Explain→Plan→Patch pipeline
- [x] Code fix pipeline: locate→context→patch→build→retry (max 3)
- [x] Repository history persistence
- [x] API provider selection (8 providers)
- [x] Spring context.GetObject resolver
- [x] Same-class private method connector (string-based, fast)
- [x] Edge dependency type classification (5 types)
- [x] Per-project semantic compilation (with fallback to per-file)

- [x] Semantic doc builder (SyntaxWalker + regex hybrid)
- [x] Embedding index (InMemoryVectorStore)
- [x] Reverse index (table/http/exception/config → method)
- [x] Code summarizer (structured behavior summary, no raw code)
- [x] Hybrid retrieval with per-profile weights (Code/Bug/Architecture/Database)
- [x] Semantic benchmark (40 ZhiFang queries, Recall/MRR/Precision/NoiseRatio)
- [x] Failure pattern analysis (7 patterns)
- [x] QueryType→Profile auto-mapping (8 types)
- [x] NoiseTermFilter (CRUD/DTO/utility penalty)
- [x] Graph expansion constraints (base skip, same-class bonus)
- [x] RetrievalTrace (per-result traceability)
- [x] RankingRuleSet (centralized rules, no scattered if/else)
- [x] NoiseContributionReport (pollution source identification)

## Retrieval Quality Targets

| Metric | Current | Target |
|---|---|---|
| Hybrid Recall@5 | ~65% | >75% |
| Precision@5 | ~52% | >70% |
| NoiseRatio | ~48% | <30% |

## Next — Phase 7B: L2 Code Modification

- Allow: same-class multi-method modification, new private fields/methods
- Forbid: public API, interface, constructor signature changes
- Context upgrade: class fields + all method signatures + target body + related private methods

## Next — Phase 7C: DI Call Chain Completion

- Parse Microsoft.Extensions.DI: AddScoped/AddTransient/AddSingleton
- Parse Autofac: RegisterType().As<>()
- Generate spring:di-bind edges

## Next — Phase 7D: Domain Knowledge Layer

- Auto-extract domain terms from entity names, table names, method names
- Generate business chain summaries per Controller method
- Inject as "project background knowledge" into LLM context

## Next — Phase 7E: Context Compression

- CodeSummarizer: per-method semantic summary (no source code)
- Token budget per scenario: query 500 / L1 fix 1500 / L2 fix 3000
- Summary cache: {methodId}-summary.json

## Non-Goals

- Cross-repository analysis (deferred)
- Multi-language support (deferred)
- Autonomous agent behavior (deferred)
- General-purpose AI IDE (out of scope)

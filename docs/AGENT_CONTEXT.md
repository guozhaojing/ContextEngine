# Agent Context

> AI coding reference. Update after every phase.

## Project

**ContextEngine** — Deterministic, grounding-verified semantic runtime for C# code intelligence.

- .NET 9.0, C#, React 18 + Tailwind + Vite frontend
- Entry: `Program.cs` (3 modes: `dotnet run` for REPL, `--web` for API, `--scan` for legacy)
- No LLM in core pipeline. LLM only used via `/api/codefix` and `/api/agent`.

## Architecture Layers (bottom-up)

```
Core.Scanning → Core.Graph → Core.Semantics / Core.Truth
  → Core.Grounding (Confidence, Contradictions)
  → Core.Cognition (Architecture/Impact/Capability/RootCause)
  → Core.Cognition.Epistemics / Patching / CodeFix
  → Core.SelfValidation / Verification
  → Core.Runtime (Governance)
  → Core.Experience / ReasoningUX / Observability
  → App.Cli / App.WebApi
```

## Key Files

| What | Where |
|---|---|
| Entry | `Program.cs` |
| Scanner | `Core/Scanning/ProjectCodeScanner.cs` |
| Graph builder | `Core/Graph/CodeGraphBuilder.cs` |
| Semantic provider | `Core/Semantics/ProjectSemanticModelProvider.cs` |
| Analyzers | `Core/Graph/Analysis/Analyzers/` (5 files) |
| Graph query | `Core/Graph/GraphQueryService.cs` |
| Cognition engines | `Core/Cognition/` (ArchitectureExplorer, ChangeImpactAnalyzer, BusinessCapabilityMapper, GroundedRootCauseExplorer) |
| Semantic docs | `Core/Cognition/SemanticDoc/` (SemanticDocBuilder, CodeSummarizer, SemanticEmbeddingService, HybridRetrievalService, ReverseIndex, NoiseTermFilter, RankingRuleSet, RetrievalTrace, NoiseContributionReport, SemanticBenchmarkRunner) |
| Code fix | `Core/Cognition/CodeFix/` (CodeFixPipeline, SymbolLocator, BuildValidator) |
| Patching | `Core/Cognition/Patching/` (PatchPlanner, ConventionAnalyzer) |
| Grounding | `Core/Grounding/` (GroundedClaimValidator, HallucinationBlocker) |
| Confidence | `Core/Grounding/Confidence/` (ConfidencePropagationEngine, EdgeConfidencePolicy) |
| Self-validation | `Core/SelfValidation/` (ResponseSelfEvaluator, EpistemicRiskAnalyzer) |
| Verification | `Core/Verification/` (VerificationOrchestrator) |
| Experience | `Core/Experience/` (QueryRouter, RepositorySession, InteractiveCognitionSession) |
| Reasoning UX | `Core/ReasoningUX/` (ReasoningPresentationEngine) |
| CLI REPL | `App/Cli/CognitionRepl.cs` |
| Web API | `App/WebApi/WebApiSessionManager.cs`, `WebApiEndpoints.cs` |
| Frontend | `webui/src/App.tsx`, `webui/src/api/client.ts` |

## Edge Kinds

| Kind | Meaning |
|---|---|
| `call` | Direct method call |
| `nh:entity-access` | NHibernate entity access |
| `spring:implements` | Spring interface impl |
| `spring:property-ref` | XML property reference |
| `spring:object-get` | context.GetObject() |
| `spring:property-inj` | XML property injection |
| `spring:generic-dao` | Generic base DAO call |

## Analyzer Registration

```csharp
new CodeGraphAnalysisOrchestrator(new IGraphAnalyzer[] {
    new AspNetRouteAnalyzer(),
    new SpringBeanAnalyzer(),
    new SpringContextObjectAnalyzer(),  // context.GetObject + XML injection
    new NHibernateAnalyzer(),
    new NhSessionGenericAnalyzer(),
});
```

## Key Constants

- All dictionary keys: `StringComparer.Ordinal`
- All iteration: `.OrderBy(..., StringComparer.Ordinal)`
- Float epsilon: 0.0001
- Target framework: `net9.0`
- Cache dir: `%LOCALAPPDATA%/ContextEngine/cache/`
- Web port: `5290` (backend), `5173` (frontend dev)
- Max graph syntax edges: 5000
- Max class connector edges: 3000

## Build Commands

```bash
dotnet build                # 0 errors expected
dotnet run -- --web         # API server
cd webui && npm run dev     # Frontend dev
```

## Coding Conventions

- No comments unless clarifying non-obvious logic
- File-scoped namespaces (`namespace X;`)
- `required` + `init` for immutable DTOs
- `readonly record struct` for value types
- `IEquatable<T>` on all state types
- No HashSet iteration without OrderBy
- Edge attributes carry `dependencyType` for edge classification
- All cognition output types: `CognitionResult.Explanations` + `Citations`

## DO NOT

- Add comments to code files
- Use `var` where type unclear
- Modify GraphNode directly from analyzer
- Use runtime reflection or LLM in core pipeline
- Introduce nondeterministic iteration order
- Add new governance/validation layers without explicit need

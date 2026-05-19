# Architecture Overview

## System Layers

```
App/Cli/          ← Developer REPL
  ↓
Core/Experience/  ← Session management, query routing, formatting
  ↓
Core/Cognition/   ← Architecture, Impact, Capability, Root Cause engines
  ↓
Core/Grounding/   ← Claim validation, confidence propagation, contradictions
  ↓
Core/Runtime/     ← Semantic state, governance, replay, provenance
  ↓
Core/Graph/        ← Code graph, indexing, traversal, query
  ↓
Core/Scanning/     ← Roslyn-based project scanning
```

## Key Namespaces

| Namespace | Purpose |
|---|---|
| `Core.Scanning` | Discover .NET projects, scan C# source to `CodeUnit` |
| `Core.Graph` | Build call graphs, index adjacency, semantic traversal |
| `Core.Graph.Analysis` | NHibernate, Spring.NET, ASP.NET route analyzers |
| `Core.Graph.Identity` | Stable `MethodId` for deterministic node identity |
| `Core.Semantics` | Roslyn symbol binding via `SymbolHandle` |
| `Core.Truth` | `TruthScore` — unified confidence/evidence model |
| `Core.Retrieval` | Hybrid retrieval, deterministic ranking, chunking |
| `Core.Context` | Context assembly, grounding validation, evidence |
| `Core.Grounding` | Claim validation, hallucination blocking, citations |
| `Core.Grounding.Confidence` | Confidence propagation, edge decay rules |
| `Core.Grounding.Contradictions` | Contradiction detection, consistency validation |
| `Core.Runtime` | Semantic state, replay fingerprint, provenance snapshot |
| `Core.Runtime.Governance` | Invariant registry, transition validation, drift detection |
| `Core.Explainability` | Ranking explanation, audit trails, evidence reporting |
| `Core.Evaluation` | E2E benchmarks, prompt quality, system consistency |
| `Core.Evaluation.Cognition` | Cognition benchmarks, workflow simulation, regression |
| `Core.Cognition` | Architecture/Impact/Capability/RootCause explorers |
| `Core.Experience` | Repository session, query routing, interactive session |
| `App.Cli` | REPL, cache, CLI tooling |

## Data Flow

```
.sln / .csproj
  ↓ ProjectCodeScanner
CodeUnit[] (methods, calls, symbols)
  ↓ CodeGraphBuilder + Analyzers
CodeGraph (nodes, edges, facts)
  ↓ GraphIndex.Build()
GraphIndex (adjacency, edges by kind)
  ↓ SymbolGraphBuilder
SymbolReferenceIndex (DocumentationCommentId → node)
  ↓
GraphQueryService (read-only query surface)
  ↓
Cognition Engines (architecture, impact, capability, root cause)
  ↓
CognitionResult (explanations + evidence citations)
```

## Determinism Architecture

All runtime-critical paths are deterministic:

| Layer | Determinism Mechanism |
|---|---|
| Identity | `MethodId` — stable content-derived ID, not UUID |
| Symbol | `SymbolHandle` — Roslyn `DocumentationCommentId` |
| Iteration | `OrderBy(x, StringComparer.Ordinal)` on all collections |
| Ranking | `DeterministicRanker` — multi-key sort with terminal tie-breaker |
| Propagation | Fixed decay rules, BFS with sorted expansion |
| State | Immutable `readonly record struct` / sealed `init`-only classes |
| Replay | `IEquatable<T>` on all state types, `ReplayFingerprint` |
| Governance | Machine-verifiable invariants, static validation rules |

## Confidence Model

```
TruthSource (Roslyn > NHibernate > Spring > Analyzer > Heuristic)
  ×
EvidenceStrength (SemanticDirect > SemanticInferred > SyntaxDirect > SyntaxPattern)
  =
TruthScore (0.0–1.0)
  →
GroundingConfidence (Certain/Strong/Moderate/Weak/Speculative/Unsupported)
```

Confidence decays through graph edges at fixed rates:
- `DirectSymbolBinding` × 1.00
- `ExplicitInvocation` × 0.95
- `ControlFlow` × 0.92
- `DataFlow` × 0.90
- `PropagationInference` × 0.60
- `SpeculativeExpansion` × 0.40

## Contradiction Types

| Type | Severity |
|---|---|
| `DirectConflict` — opposing claims about same subject | Severe |
| `ShadowAbstraction` — abstraction without evidence | Severe |
| `UnsupportedInference` — inference without evidence | Severe |
| `ConfidenceConflict` — confidence gap on same subject | Moderate |
| `SemanticDrift` — diverging semantics on same symbol | Moderate |
| `StaleGrounding` — speculative ancestry in evidence | Moderate |
| `DivergentImplementation` — non-overlapping evidence | Mild |

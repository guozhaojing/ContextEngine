# ContextEngine

A deterministic, provenance-aware, grounding-verified semantic runtime for trustworthy code intelligence.

## What It Does

ContextEngine analyzes .NET solutions and provides:
- **Architecture understanding** — identify subsystems, layers, and integration points
- **Change impact analysis** — determine what breaks when you modify code
- **Business capability mapping** — discover where business logic is implemented
- **Grounded root cause analysis** — debug runtime behavior with evidence

All explanations are citation-backed and confidence-scored.

## Quick Start

```bash
# Enter interactive cognition REPL
dotnet run

# Load a repository
cognition> load D:\Projects\MySolution

# Ask natural engineering questions
cognition> ask "Explain the payment architecture"
cognition> ask "What breaks if I change RetryPolicy?"
cognition> ask "Why does reconnect fail after timeout?"

# Follow up
cognition> followup "Who depends on it?"

# Export investigation
cognition> export report.md
```

### With a pre-loaded repository

```bash
dotnet run -- --load D:\Projects\MySolution
```

### Legacy scan mode

```bash
dotnet run -- --scan D:\Projects\MySolution
```

## Key Principles

- **Deterministic**: Same codebase + same query = identical results every time
- **Grounded**: Every claim is backed by source code evidence
- **Provenance-aware**: Every explanation traces to specific files and symbols
- **Confidence-calibrated**: Uncertainty is explicit, not hidden
- **Replayable**: Results can be compared across runs for regression testing

## Architecture

```
Developer Query
  ↓
DeveloperQueryInterpreter → intent classification
  ↓
QueryRouter → route to cognition engine
  ↓
ArchitectureExplorer / ChangeImpactAnalyzer /
BusinessCapabilityMapper / GroundedRootCauseExplorer
  ↓
CognitionResult → explanations + citations
  ↓
CognitionResponseFormatter → readable output
```

## Supported Project Types

- C# .NET solutions (.sln)
- Individual projects (.csproj)
- Directory trees with multiple projects
- NHibernate-based data access layers
- Spring.NET dependency injection
- ASP.NET route-annotated controllers

## Requirements

- .NET 8.0 SDK
- Target: .NET solutions using C#

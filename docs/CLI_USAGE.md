# CLI Usage Guide

## Starting the REPL

```bash
dotnet run                          # Interactive REPL
dotnet run -- --load <path>         # Load repo and enter REPL directly
dotnet run -- --scan <path>         # Legacy full pipeline scan mode
```

## Commands

| Command | Description |
|---|---|
| `load <path>` | Load a .NET solution or project |
| `reload` | Reload current repository (skip cache) |
| `ask <question>` | Ask a natural engineering question (auto-routed) |
| `followup <question>` | Follow up on previous question with context |
| `arch <question>` | Force architecture exploration |
| `impact <question>` | Force change impact analysis |
| `capability <question>` | Force business capability mapping |
| `debug <question>` | Force root cause analysis |
| `summary` | Show investigation summary |
| `history` | Show query history |
| `export <file.md>` | Export investigation report |
| `cache` | Show cache status |
| `clear` | Reset conversation context |
| `stats` | Show repository statistics |
| `help` | Show help |
| `exit` / `quit` | Exit |

## Query Routing

Questions are automatically routed to the right engine:

| Pattern | Routed to |
|---|---|
| "explain architecture", "how is structured" | ArchitectureExplorer |
| "what breaks", "who depends", "impact of" | ChangeImpactAnalyzer |
| "where is", "how does", "how is handled" | BusinessCapabilityMapper |
| "why does", "why is", "debug", "fail", "error" | GroundedRootCauseExplorer |

## Confidence Levels

Responses include a confidence indicator:

```
Certain    [██████████]  Semantic evidence + symbol binding
Strong     [████████░░]  Graph evidence + source files
Moderate   [██████░░░░]  Partial evidence
Weak       [████░░░░░░]  Limited evidence
Speculative[██░░░░░░░░]  Inferred, not directly grounded
Unsupported[░░░░░░░░░░]  No evidence
```

## Response Format

Each response includes:
1. **Title** — derived from query type
2. **Confidence bar** — visual confidence indicator
3. **Evidence summary** — citation count
4. **Explanations** — numbered, confidence-tagged findings
5. **Sources** — layered citation blocks with source files
6. **Contradiction warnings** — surfaced when applicable

## Example Workflow

```
cognition> load D:\Projects\MyApp
Loading: D:\Projects\MyApp
  Loaded from cache.
Repository loaded. (0.3s)
  Projects:  5
  Nodes:     2431
  Edges:     8912
  Facts:     3847

cognition> ask "Explain the payment architecture"
[response with architecture layers, integration points, entity counts]

cognition> followup "What are the integration points?"
[response with cross-project dependencies]

cognition> impact "What breaks if I change PaymentService?"
[response with downstream impact, risk scores, upstream dependents]

cognition> summary
Interactive Session: interactive-143021
Total Questions: 3
  Architecture: 2
  Impact Analysis: 1

cognition> export payment-investigation.md
Report exported to: D:\Projects\MyApp\payment-investigation.md
```

## Cache

First load performs full analysis. Subsequent loads restore from cache in `%LOCALAPPDATA%\ContextEngine\cache\`.

Use `reload` to force re-scan and clear cache.

## Tips

- Be specific — "Explain the payment retry architecture" beats "Explain"
- Use follow-ups — the session remembers context between questions
- Export long sessions — `export report.md` saves the full investigation
- Check confidence — moderate/low confidence may need source verification
- Prefer `impact` for refactoring questions, `debug` for failure questions

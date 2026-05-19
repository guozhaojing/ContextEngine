# Debugging Guide

## Workflow Patterns

### Debug a failure

```
cognition> debug "Why does reconnect fail after timeout?"
→ Root cause analysis with failure paths and external dependencies
cognition> followup "What are the other affected components?"
→ Impact analysis on affected nodes
cognition> followup "How do I fix this?"
→ Suggested remediation paths
```

### Analyze change impact

```
cognition> impact "What breaks if I change RetryPolicy?"
→ Downstream impact with risk scores (Low/Medium/High/Critical)
cognition> followup "Who depends on RetryPolicy?"
→ Upstream entry point analysis
cognition> followup "What is the risk level?"
→ Aggregate risk assessment
```

### Explore architecture

```
cognition> arch "Explain the payment architecture"
→ Subsystems, layers, integration points
cognition> followup "What are the integration points between subsystems?"
→ Cross-project dependency analysis
cognition> followup "Show me the dependency graph"
→ Execution topology summary
```

### Discover business logic

```
cognition> capability "Where is invoice sync implemented?"
→ Service classes mapped to capabilities
cognition> followup "What services does it call?"
→ Downstream callee enumeration
cognition> followup "Show the execution chain"
→ Multi-hop call path
```

## Confidence Interpretation

| Level | Meaning | Action |
|---|---|---|
| Certain / Strong | Well-grounded. Symbol-bound, source files present. | Trust the explanation. |
| Moderate | Partial evidence. Some sources found. | Verify against source code. |
| Weak | Limited evidence. Graph may be incomplete. | Treat as hints, verify independently. |
| Speculative | Inferred, not directly grounded. | Treat as hypotheses only. |
| Unsupported | No evidence. | Generation suppressed. Rephrase query. |

## Common Issues

### "No root cause found"
- Rephrase with specific method/class names
- Try broader: "What services handle payment?"
- Then narrow down with `impact` on found services

### "No architecture layers detected"
- Solution may be too small or not using recognizable patterns
- Try: "Explain the structure of project X" with a specific project name

### "Low confidence"
- The code graph may not have enough information
- Check if analyzers detected framework patterns (NHibernate, Spring)
- Try forcing a specific engine: `arch`, `impact`, `capability`, `debug`

### Cache is stale
- Use `reload` to force re-scan
- Use `cache` to check cache status

## Exporting for Sharing

```
cognition> export debugging-session.md
```

Exported files include:
- Full query history
- All explanations with confidence levels
- Evidence citations with source files
- Investigation summary

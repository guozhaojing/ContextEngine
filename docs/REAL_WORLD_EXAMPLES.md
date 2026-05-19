# Real-World Examples

## Scenario 1: Onboarding to a Payment System

```
cognition> load D:\Projects\PaymentHub

cognition> ask "Explain the payment processing architecture"
```

**Output:**
```
# Architecture Overview

**Confidence:** Strong
```
[████████░░]  85%
```
*12 source(s) support this explanation.*

The system comprises 3 subsystem(s) with 8 API entry point(s).

PaymentHub: 5 service(s), 2 controller(s), 3 entry point(s), 4 entity node(s).
PaymentGateway: 3 service(s), 1 controller(s), 2 entry point(s), 2 entity node(s).
PaymentSettlement: 4 service(s), 0 controller(s), 1 entry point(s), 3 entity node(s).

API Route Layer: 8 node(s). HTTP endpoint entry points
Controller Layer: 3 node(s). Request handling and routing
Service Layer: 12 node(s). Business logic and orchestration
Entity/Data Layer: 9 node(s). Data access and persistence

Cross-project integration: PaymentHub ↔ PaymentGateway
Cross-project integration: PaymentHub ↔ PaymentSettlement
```

```
cognition> followup "How does PaymentService handle retries?"
```

## Scenario 2: Refactoring Risk Assessment

```
cognition> impact "What breaks if I change PaymentGateway.RetryPolicy?"
```

**Output:**
```
# Change Impact Analysis

**Confidence:** Moderate
```
[██████░░░░]  60%
```
*5 source(s) support this explanation.*

Change target(s): PaymentGateway.RetryPolicy. Downstream impact: 7 node(s). Upstream dependents: 3 node(s).

[High] PaymentService.SubmitPayment: hop=1, confidence=0.85, risk=0.65
[Medium] SettlementService.ProcessBatch: hop=2, confidence=0.72, risk=0.48
[Medium] NotificationService.SendReceipt: hop=3, confidence=0.55, risk=0.40
[Low] AuditService.LogTransaction: hop=4, confidence=0.42, risk=0.30

MODERATE RISK: 3 notable impact(s). Changes require careful review.

Entry point depends on target: PaymentController.Submit (via call)
Entry point depends on target: SettlementController.Process (via call)
```

```
cognition> followup "What is the risk of changing the timeout?"
```

## Scenario 3: Debugging a Production Issue

```
cognition> debug "Why does transaction sync fail intermittently?"
```

**Output:**
```
# Root Cause Analysis

**Confidence:** Moderate
```
[██████░░░░]  60%
```
*3 source(s) support this explanation.*

Diagnosis targets: 2 method(s). Root cause hypotheses: 3.

[Strong] SyncService.SynchronizeTransaction: has 4 caller(s) and 5 callee(s) — check the execution path through SyncService → TransactionRepo → ExternalApiClient
[Strong] Depends on external node(s): ExternalPaymentApi — external failure could propagate.
[Moderate] Contradiction: 'SyncService.SynchronizeTransaction' called alongside: CacheService.Invalidate, QueueService.Enqueue — potential interaction failure point.

Execution path analysis: SyncService.SynchronizeTransaction → TransactionRepo.GetPending → ExternalPaymentApi.Verify
External dependency: SyncService.SynchronizeTransaction calls 2 external node(s): ExternalPaymentApi.Verify, NotificationHub.Publish

> ⚠ Qualified Confidence
> This explanation has moderate or lower confidence. Evidence is partial.
> Consider verifying against the source code directly.
```

## Scenario 4: Architecture Exploration

```
cognition> arch "Explain the data access layer"
```

**Output:**
```
# Architecture Overview

Entity/Data Layer: 9 node(s). Data access and persistence
Service Layer: 12 node(s). Business logic and orchestration

TransactionRepository: accesses entity/data; calls: GetPending, MarkComplete, FindByStatus
PaymentRepository: accesses entity/data; calls: Save, FindById, FindByReference
SettlementRepository: accesses entity/data; calls: BatchInsert, FindUnsettled
```

## Scenario 5: Full Investigation Export

```
cognition> load D:\Projects\MyApp
cognition> ask "Explain the system"
cognition> impact "What depends on the main service?"
cognition> debug "Why does retry fail?"
cognition> followup "What are the affected components?"
cognition> summary
cognition> export full-investigation.md
```

The exported `full-investigation.md` contains the complete session with all questions, explanations, evidence, and citations.

## Tips for Better Results

1. **Be specific** — "Explain how PaymentService handles timeouts" beats "Explain payment"
2. **Use method/class names** — the system searches the code graph by name
3. **Chain questions** — start broad, narrow down
4. **Check confidence** — if weak, try rephrasing or a different engine
5. **Export long sessions** — investigation reports are useful for documentation
6. **Use follow-up** — context carries over: "Who depends on IT?" resolves "it" to the last target

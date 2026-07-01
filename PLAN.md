# PLAN — Durable-orchestration sample journey (agreed, not built)

The next chunk of work. See `CLAUDE.md` for the project rules, gotchas, and current status.

One claim flowing end-to-end: **Comms → Tasks (durable) → fan-out to the 3 bricks → back**, carrying
CorrelationId scope, a linked trace, per-service metrics, and durable task state in Cosmos.

## Components
- **Comms** (built): on trigger, publish an event to the Tasks queue (carrying `CorrelationId`).
- **`ClaimFlow.Tasks`** — Durable Functions (isolated). This is the WSM/Orchestrator.
  - *Starter* (Service Bus trigger from Comms): log + metric + write the **first task doc** to Cosmos,
    then start the orchestration with **`instanceId = CorrelationId`**.
  - *Orchestrator function* (deterministic): for each brick, publish a request (via an **activity**) and
    `WaitForExternalEvent` the reply; write a task doc per stage to Cosmos; **replay-safe logging**;
    **no metric**.
  - *Response handler* (Service Bus trigger on `orchestrator-responses`): read the brick reply and
    `RaiseEventAsync(instanceId = CorrelationId, "<Stage>Done", payload)` to resume the right orchestration.
- **Classifier / Preparer / Filer** — each mirrors Comms: SB trigger → log → metric → call an injected
  service → `Task.Delay` fake work → publish the response back with `CorrelationId + Stage + Status` → metric.

## Key design decisions
- **`CorrelationId` IS the orchestration instanceId** — one id does response routing *and* the Cosmos
  partition key; no separate orchestrationId to carry.
- **Two separate stores.** Durable Functions keeps its own runtime state in **Azurite /
  `AzureWebJobsStorage`**; the app's **task documents** are separate Cosmos docs, **partition key =
  CorrelationId** (one partition per claim, one doc per stage).
- **Orchestrator determinism.** No direct I/O in the orchestrator — Cosmos writes and message publishing
  go through **activities**; log via `context.CreateReplaySafeLogger(...)` to avoid replay duplicates
  (the durable analog of the isolated-worker duplicate-log gotcha).
- **Metrics:** bricks + Comms emit business counters; the orchestrator does **not** (coordination only,
  just logs + Cosmos).

## Open questions to resolve when building
- Durable **isolated** extension + Aspire wiring against installed 13.2.4 (verify APIs; likely another
  preview edge).
- **Fan-out vs sequential** to the 3 bricks (diagram implies parallel fan-out via `Task.WhenAll`).
- Whether bricks share one meter or use per-service meters (Comms currently uses its own `ClaimIntakeMetrics`).

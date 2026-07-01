# CLAUDE.md — ClaimFlow Observability Lab

## What this is

A **mock** claim-processing pipeline whose only purpose is to prove out observability
patterns end-to-end: OpenTelemetry logs/metrics/traces, a business `CorrelationId` that flows
through the log **scope** across process boundaries (including from an Azure Function into an
injected service), an append-only audit trail, and per-task state tracking in Cosmos DB.

**No real data.** Every "processing" step is faked (random delays, canned results). Do not add
real document parsing, real AI calls, or real filing. This repo exists to validate plumbing and
observability, nothing else.

The design mirrors a real system. We use **generic names** (mapping is just context):

| Real stage | This repo | Role |
|---|---|---|
| Comms (email intake) | **Intake** | Creates the `CorrelationId`, emits the first message |
| WSM (work state mgmt) | **Orchestrator** | Orchestrates the flow, tracks task state in Cosmos, Blazor UI |
| DocInt (AI classify/extract) | **Classifier** | Mock brick |
| Automation (prepare claim) | **Preparer** | Mock brick |
| Filer (file into core) | **Filer** | Mock brick |
| Audit sink | **Audit** | Consumes audit events, stores in Cosmos, Blazor UI |

---

## How we work — the staged control loop

Build in **small stages**. The human stays in control at every checkpoint. Do not run ahead.

For each stage:

1. **Agree scope.** State the stage's scope + its single **acceptance check** (the concrete thing
   the human looks at to decide it's done). Wait for the human to confirm or adjust.
2. **Build small.** Produce only that stage — small enough to read in one sitting.
3. **Human verifies** against the acceptance check (usually something visible in the Aspire dashboard
   or a UI).
4. **Human says pass or adjust.** On "adjust", fix within the same stage. Do **not** advance.
5. **Advance only on explicit go.** Update `## Current status` when a stage passes.

The human is guiding to the result: nothing proceeds past a checkpoint they didn't personally sign off.

---

## Goals — what "done" means

A run of a single claim must demonstrate all of the following in the Aspire dashboard:

1. **CorrelationId in log scope.** Intake generates a `CorrelationId` (GUID). It rides every message
   as a Service Bus application property. Every service opens a log scope with it on receipt. Every
   subsequent log line in that async flow — **including logs from a separate injected service** —
   carries the `CorrelationId`, without it being passed around by hand.
2. **Distributed trace.** One trace spans Intake → Orchestrator → each brick → back, stitched
   automatically via OTel trace-context propagation over Service Bus.
3. **Metrics.** A custom meter emits per-stage counters (success/fail) and a latency histogram.
4. **Audit trail.** Each stage publishes an audit event; Audit persists to Cosmos, queryable by
   `CorrelationId`.
5. **Task state.** Orchestrator persists per-claim task state to Cosmos, shown in its UI.

---

## Tech stack

- **Aspire 13.4.x** on **.NET 10** (renamed from ".NET Aspire"; CLI is `aspire`).
- **Azure Service Bus emulator** for all messaging (`RunAsEmulator()` — emulator container +
  companion SQL Server container).
- **Azure Cosmos DB Linux preview emulator** (`RunAsPreviewEmulator()` + Data Explorer).
- **Blazor** for the Orchestrator and Audit UIs.
- **OpenTelemetry** via `ServiceDefaults` (Aspire wires logs/metrics/traces to the dashboard).
- **Intake** ends up as an **Azure Functions (isolated)** app — but is built as a worker first and
  ported later (see stages).

> ⚠️ Aspire/Azure integration APIs move between versions. **Do not trust method/package names from
> memory.** Verify every AppHost API and package ID against the installed version before using it.
> If a snippet in this file doesn't compile, fix it against the installed API — don't guess.

---

## Shared foundation — build and PROVE this FIRST (Stage 0)

This is the heart of the project. Before any real service, we build a **reusable middleware layer**
that owns *all* cross-cutting observability + messaging concerns, and a **system test** that proves
it works. Every later service just references it and implements "do the work" — nothing reimplements
scope, tracing, metrics, or publishing.

### Foundation projects

**`ClaimFlow.ServiceDefaults`** — Aspire's shared defaults, extended:
- OpenTelemetry logging, metrics, tracing → OTLP exporter to the dashboard.
- **`IncludeScopes = true`** on logging (non-negotiable — without it `CorrelationId` never appears).
- Register the app's `ActivitySource` and `Meter` names with OTel.
- Add Azure Service Bus trace instrumentation so `traceparent` links across message hops.
- Standard health checks + service discovery.

**`ClaimFlow.Contracts`** — shared, dependency-light:
- Application-property key constants: `CorrelationId`, `Stage`, `Status`.
- Queue-name constants and telemetry names (`ActivitySourceName`, `MeterName`).
- Message DTOs (small JSON records — fake claim id + minimal fields) and the audit-event DTO.

**`ClaimFlow.Messaging`** — the middleware (the important new piece):
- **Receive middleware.** A single reusable wrapper around `ServiceBusProcessor` that, for *every*
  received message, in order:
  1. reads `CorrelationId` + `Stage` from `ApplicationProperties`,
  2. opens `logger.BeginScope({ CorrelationId, Stage })` for the whole unit of work,
  3. records a start metric + starts/continues the trace activity,
  4. invokes the service's handler delegate (the only thing a service writes),
  5. emits an audit event + success/latency metrics on completion,
  6. on exception → dead-letters + failure metric, all still inside the scope.
- **Publisher.** `IMessagePublisher.PublishAsync(queue, payload, correlationId, stage)` that sets the
  app properties consistently so no service hand-rolls property names.
- **Base handler abstraction.** Services implement one method (`HandleAsync(claim, ct)`); scope,
  tracing, metrics, and audit come for free from the middleware.
- **DI extensions.** e.g. `builder.AddClaimFlowMessaging()` registers the SB client, the processor
  host, the publisher, and the meter/activity source. A service's `Program.cs` should be a few lines.

Illustrative shape (verify APIs against installed version — this is the pattern, not gospel):

```csharp
// In the shared receive middleware, per message:
var correlationId = msg.ApplicationProperties[Props.CorrelationId] as string ?? Guid.NewGuid().ToString("N");
var stage = msg.ApplicationProperties[Props.Stage] as string ?? "unknown";

using (logger.BeginScope(new Dictionary<string, object>
{
    [Props.CorrelationId] = correlationId,
    [Props.Stage] = stage,
}))
{
    using var _ = Telemetry.MeasureStage(stage);           // metric histogram
    try
    {
        await handler.HandleAsync(payload, ct);            // <-- the ONLY thing services write
        await publisher.AuditAsync(correlationId, stage, Status.Ok, ct);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Stage {Stage} failed", stage);
        await publisher.AuditAsync(correlationId, stage, Status.Failed, ct);
        await args.DeadLetterMessageAsync(msg);
        throw;
    }
}
```

The key property: neither the handler nor any service it injects is passed the `CorrelationId` — it
comes purely from the scope the middleware opened, and flows via `AsyncLocal`.

### The system test (Stage 0 acceptance)

A minimal live slice that exercises the middleware, using the first two real endpoints as the harness
so nothing is throwaway:

- **Intake** (worker) — creates a `CorrelationId`, has an **injected service** that also logs, then
  publishes one message via the shared publisher.
- **Classifier** — receives via the shared receive middleware and its handler logs.
- **AppHost** — SB emulator + the one queue.

**Acceptance check (the human eyeballs this in the dashboard):**
1. The same `CorrelationId` appears on: Intake's log, Intake's **injected service's** log, and
   Classifier's handler log — none of which were passed the id explicitly.
2. A single trace links Intake → Classifier.
3. The stage metric incremented.

If (1) fails, the cause is almost always `IncludeScopes`. Fix the foundation, not the services.

**Nothing else is built until Stage 0 passes.** Every later stage is repetition of a pattern the
human has already approved.

---

## Architecture (full pipeline, built on the foundation)

```
Intake ──> Orchestrator ──> Classifier ─┐
                        ──> Preparer   ─┼──> (responses) ──> Orchestrator
                        ──> Filer      ─┘
   every service ─────────────────────────> Audit
```

## Messaging topology

All communication is Service Bus **queues** (not topics — keep it simple for the demo):

- `orchestrator-in` — Intake → Orchestrator
- `classifier-in`, `preparer-in`, `filer-in` — Orchestrator → each brick
- `orchestrator-responses` — every brick → Orchestrator (matched by `CorrelationId` + `Stage`)
- `audit-in` — every service → Audit

Every message carries application properties `CorrelationId`, `Stage`, and (on responses) `Status`.
Bodies are small JSON DTOs. No real payloads.

## The observability contract

Implemented **once, in the shared middleware** — services never reimplement it. Two independent
mechanisms; the demo must show both:

1. **Business CorrelationId → log scope** (Stage 0 above). `IncludeScopes = true` is mandatory.
2. **OTel trace context.** `Azure.Messaging.ServiceBus` auto-propagates `traceparent`. Do **not**
   hand-roll trace propagation; if traces don't link, fix instrumentation in ServiceDefaults.

Metrics: one `Meter` (e.g. `ClaimFlow.Pipeline`) with per-stage counters + a duration histogram,
registered with OTel in ServiceDefaults.

---

## Solution structure

```
ClaimFlow.sln
  src/
    ClaimFlow.AppHost/           # Aspire orchestration: SB + Cosmos resources, project wiring
    ClaimFlow.ServiceDefaults/   # OTel (logs+metrics+traces), IncludeScopes, health, SB tracing
    ClaimFlow.Contracts/         # message DTOs, app-property + queue + telemetry name constants
    ClaimFlow.Messaging/         # THE MIDDLEWARE: receive scope/metrics/audit, publisher, DI ext
    ClaimFlow.Intake/            # worker + injected service first  ->  Azure Function later
    ClaimFlow.Orchestrator/      # worker + Blazor UI + Cosmos task store
    ClaimFlow.Classifier/        # mock brick
    ClaimFlow.Preparer/          # mock brick
    ClaimFlow.Filer/             # mock brick
    ClaimFlow.Audit/             # worker + Blazor UI + Cosmos audit store
```

- The three bricks are **near-identical** — thanks to the middleware, each is basically one
  `HandleAsync`. Write one, copy-rename, keep them in sync.
- Producer and consumer never drift on names because both use `ClaimFlow.Contracts`.

## Coding conventions

- C# latest, nullable enabled, file-scoped namespaces, primary constructors where they read well.
- Consumers go through the shared middleware, never a raw receive loop.
- Each service's `Program.cs` is minimal: `AddServiceDefaults()`, `AddClaimFlowMessaging()`, register
  the handler (+ Blazor for the two with UIs).
- Fake work = `Task.Delay(Random.Shared.Next(200, 800))` + a canned result. Never add real logic.

## Critical gotchas

- **IncludeScopes.** If `CorrelationId` isn't in the logs, this is why. Check first, in ServiceDefaults.
- **Cosmos preview emulator.** `RunAsPreviewEmulator(e => e.WithDataExplorer())`; suppress
  `ASPIRECOSMOSDB001`. Flakiest resource: slow first start, SSL-cert regen on boot, persistence
  quirks. Pull the latest `vnext` image before assuming a code bug. Access via `CosmosClient` / the
  Aspire Cosmos client integration — **do not** use a Cosmos change-feed trigger (broken vs emulator).
- **Service Bus emulator** brings a SQL Server sidecar; first cold start is slow. Use
  `ContainerLifetime.Persistent` on the emulators to avoid re-pulling each run.
- **Azure Functions in Aspire is still preview.** Keep Functions tooling current. (Note: the human
  chose to make Comms a Function *early* rather than worker-first — see below for what that cost.)
- **API drift.** Re-confirm AppHost/client APIs against the **installed 13.2.4** packages, not these
  snippets and not 13.4.x. Verify extension-method namespaces by decompiling (`ilspycmd`) when a
  `using` isn't obvious — e.g. `UseFunctionsWorkerDefaults` lives in
  `Microsoft.Azure.Functions.Worker.OpenTelemetry`.
- **Isolated-worker telemetry (duplicate logs + flat traces).** With a .NET-isolated Function under
  Aspire, both the Functions *host* and the *worker* export to the same OTLP endpoint, so every user
  log appears **twice** (worker's scoped copy + host's stripped relay) and the trace has only the
  host's HTTP span. Fix, all three needed: (1) `host.json` → `"telemetryMode": "OpenTelemetry"`;
  (2) add `Microsoft.Azure.Functions.Worker.OpenTelemetry` and call
  `builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults()`; (3) align
  `Microsoft.Azure.Functions.Worker` to **2.52.0** (Core/Grpc/Worker must match) or you get a runtime
  `MissingMethodException` on `DefaultTraceContext..ctor` (500s). Same root cause as the classic
  App Insights double-logging.
- **Aspire resource names can't start with a digit** (`ASPIRE006`). Use a letter prefix for flow
  order, e.g. `s1-comms`, `s2-orchestrator` — not `1-comms`.
- **Prune-package data missing (`NETSDK1226`)** on .NET 10 SDK 10.0.1xx with the AspNetCore
  FrameworkReference. Fixed repo-wide via `AllowMissingPrunePackageData=true` in `Directory.Build.props`.
- **Verifying telemetry without the GUI:** `aspire otel logs|spans|traces <resource>` (add
  `--format Json` for attributes/scopes/trace ids). There is **no** `aspire otel metrics`, and
  `aspire export` omits metrics — metrics must be eyeballed in the dashboard Metrics tab.
- **Don't `pkill -f` a pattern that matches your own launch command** (e.g. `"aspire run"` or
  `"ClaimFlow.AppHost"`) — it SIGTERMs the shell doing the kill. Kill in a step separate from launch.

---

## Stages

Build vertical slices; run (`aspire run`) and verify each before the next. Each stage lists its
**acceptance check** (human-owned) and **what the human steers**.

- [ ] **Stage 0 — Shared foundation + system test.** ServiceDefaults, Contracts, Messaging
      middleware, and the Intake→Classifier harness that proves the middleware.
      - *Acceptance:* the three-part CorrelationId proof + linked trace + a metric (see system test).
      - *Human steers:* the scope/publisher/handler pattern that becomes the standard everywhere.
      - **Gate: nothing advances until this is green.**
- [ ] **Stage 1 — Orchestrator + fan-out.** Insert Orchestrator between Intake and the three bricks;
      bricks reply on `orchestrator-responses`.
      - *Acceptance:* one trace spans Intake → Orchestrator → all bricks → back; correlation on every hop.
      - *Human steers:* queue topology, how responses are matched.
- [ ] **Stage 2 — Cosmos task state + Orchestrator UI.** Persist per-claim state; Blazor UI.
      - *Acceptance:* state visible in the UI and in Cosmos Data Explorer.
      - *Human steers:* which state fields matter.
- [ ] **Stage 3 — Metrics.** Per-stage counters + latency histogram surfaced in the dashboard.
      - *Acceptance:* metrics show up in the dashboard metrics view.
      - *Human steers:* which metrics are worth emitting.
- [ ] **Stage 4 — Audit brick + UI.** Audit consumes `audit-in`, stores in Cosmos, Blazor UI.
      - *Acceptance:* events queryable by `CorrelationId` in the Audit UI.
      - *Human steers:* audit event shape.
- [ ] **Stage 5 — Port Intake to an Azure Function.** Same behavior behind a Service Bus trigger.
      - *Acceptance:* the Stage 0 correlation proof still holds from *inside* the Function.
      - *Human steers:* whether it's worth doing at all, or if the worker is enough.

## Commands

```bash
aspire new          # scaffold AppHost + ServiceDefaults (interactive template picker)
aspire add          # add integrations (verify exact package IDs for 13.4):
                    #   Aspire.Hosting.Azure.ServiceBus, Aspire.Hosting.Azure.CosmosDB
aspire run          # run the whole app + dashboard

# consumer-side client packages (per service that talks to SB / Cosmos):
#   Aspire.Azure.Messaging.ServiceBus, Aspire.Microsoft.Azure.Cosmos
```

Requires Docker Desktop (or Podman) running for the emulators. Pre-pull the emulator images once so
the first `aspire run` isn't blocked on downloads.

## How to work in this repo (agent instructions)

1. **Follow the control loop.** Agree scope + acceptance for a stage, build only that, wait for the
   human's pass. Never advance past an unapproved checkpoint.
2. **Verify APIs before building.** Confirm Aspire/Azure API + package names against installed
   versions; don't rely on memory or the snippets here if they don't compile.
3. **Cross-cutting concerns live in the middleware, once.** Scope, tracing, metrics, audit, publishing
   belong in `ClaimFlow.Messaging`/`ServiceDefaults`. Services only implement `HandleAsync`. If you
   catch yourself adding scope/metric code inside a service, move it to the middleware.
4. **Keep it mock.** No real parsing/AI/filing — that's out of scope.
5. **Update `## Current status`** as stages complete so the next session has context.

## Current status

**Pre-Stage-0 exploration slice — DONE and verified.** We did *not* build the full Stage 0
(no Service Bus, no Classifier, no `ClaimFlow.Messaging` middleware yet). Instead we proved the
observability plumbing end-to-end on a single service, and the human pulled the Azure Function
forward from Stage 5.

What exists and works (verified live in the Aspire dashboard + via `aspire otel`):

- **Solution** (`ClaimFlow.slnx`): `ClaimFlow.AppHost`, `ClaimFlow.ServiceDefaults`, `ClaimFlow.Comms`.
  - `Directory.Build.props` sets `AllowMissingPrunePackageData=true` (NETSDK1226 workaround, see gotchas).
- **`ClaimFlow.Comms`** (was "Intake" — renamed to the real domain name): an **Azure Functions
  isolated worker** with an HTTP-trigger `Starter` function. It creates a `CorrelationId`, opens a
  `BeginScope`, logs, and calls a DI-injected `ICommsService` that logs too. Proven: the same
  `CorrelationId` rides **both** log lines (the function's `FunctionContext` logger *and* the
  service's DI logger) via the AsyncLocal scope — the service is never handed the id.
- **Telemetry integration fixed** (isolated worker + Aspire): logs are single (not duplicated) and
  the function work is a **child span** in the host's trace. See the new "isolated-worker telemetry"
  gotcha for the how/why.
- **Metrics**: `ClaimIntakeMetrics` (an `IMeterFactory` singleton in ServiceDefaults) exposes an
  `EmailReceived` counter, registered with OTel via `AddClaimFlowMetrics()` and incremented at the
  intake checkpoint. Meter name held in one const (`Telemetry.MeterName`) used by both `Create(...)`
  and `AddMeter(...)`.
- **`ClaimFlow.Shared`/`Contracts` was created then removed** — telemetry names live next to the
  meter in ServiceDefaults for now. A dependency-light shared project should come **back at Stage 0**
  for message DTOs, queue names, and app-property keys (things multiple services must share).

Installed toolchain (verify, don't assume): **.NET SDK 10.0.108**, **Aspire CLI 13.2.4** (NOT 13.4.x —
use 13.2.x package versions), Docker running, **Azure Functions Core Tools 4.12.1** installed under
`~/.local` (npm global needed manual zip extraction), Azurite present.

**Next (real Stage 0):** agree scope + acceptance, then build `ClaimFlow.Contracts` (DTOs/queue names),
`ClaimFlow.Messaging` middleware (receive scope/metrics/audit + publisher), add the Service Bus
emulator, and wire Comms → Classifier to prove the middleware. The generic per-stage metrics
(success/fail + latency) belong in that middleware, not in services.
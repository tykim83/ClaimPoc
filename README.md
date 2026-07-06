# ClaimFlow

A mock claim-processing pipeline. Every "processing" step is fake (random delays, canned
results) — the point was never the claims, it was to prove out the observability plumbing
end-to-end on Aspire + Azure Functions (isolated) + Service Bus + Cosmos, before doing it
for real.

```
Comms (HTTP) ──► orchestrator-in ──► Tasks (durable orchestrator)
                                        │  classifier-in / preparer-in / filer-in
                                        ▼
                                     3 bricks ──► orchestrator-responses ──► Tasks
Tasks ──► Cosmos event log ◄── Dashboard (Blazor, also scrapes metrics from an OTel collector)
```

## What we wanted to prove

**A business `CorrelationId` on every log line of a claim's journey, without ever passing
it to a logger by hand.** Comms mints one GUID per claim; from then on it rides messages as
a Service Bus application property, and every log line — across five processes, including
logs from plain DI-injected services that never see the id — carries it. That works because
of log scopes (`AsyncLocal` under the hood) plus `IncludeScopes = true` on the OTel logger.

Along with that: one distributed trace per claim (the Service Bus SDK propagates
`traceparent` on its own, no hand-rolling), per-stage counters fanned out to both the Aspire
dashboard and an OTel collector, and an append-only event log in Cosmos keyed by
`correlationId`.

## CorrelationScopeMiddleware

The interesting piece is [CorrelationScopeMiddleware](src/ClaimFlow.ServiceDefaults/CorrelationScopeMiddleware.cs):
one Functions worker middleware, registered once per app, that opens the
`CorrelationId` log scope around every invocation regardless of trigger. It hunts for the
id in the binding data, in order:

1. **HTTP header** — `x-correlation-id` (or `CorrelationId`), for callers that already have one.
2. **Any JSON object in the binding data with a top-level `CorrelationId`** — this one clause
   covers both Service Bus triggers (application properties show up in binding data as JSON)
   and durable *activity* inputs (the activity's input record carries the id).
3. **The durable `instanceId`** — orchestrations are started with `instanceId = correlationId`,
   so orchestrator invocations (and durable-internal ones) get the scope too.

It deliberately never mints an id. Minting belongs to the system edge (the HTTP entry
function); mid-pipeline a missing id is a bug we want to see, not paper over.

The middleware only *finds* ids — the caller has to put it somewhere the binding data
exposes. Per trigger:

- **HTTP trigger** — the id goes in a **request header**, either `x-correlation-id` or
  `CorrelationId` (case 1). Nothing in the body or query string is looked at:

  ```bash
  curl -H "x-correlation-id: <id>" https://<host>/api/<function>
  ```

- **Service Bus trigger** — the id goes in the message's **application properties**, key
  `CorrelationId` — not in the body. The properties surface as a JSON object in binding
  data, so case 2 finds it:

  ```csharp
  new ServiceBusMessage(body) { ApplicationProperties = { ["CorrelationId"] = id } }
  ```

- **Durable client (starter)** — the function *hosting* the durable client gets its own
  scope from whatever trigger fired it (HTTP header, SB property — see above); being a
  starter adds nothing. What you must do here is pass the id **as the orchestration's
  `InstanceId`** — that is the only thing the orchestrator invocation will carry:

  ```csharp
  await durableClient.ScheduleNewOrchestrationInstanceAsync(
      "MyOrchestrator", input: null,
      new StartOrchestrationOptions { InstanceId = correlationId });
  ```

- **Durable orchestrator** — you pass nothing and there is nowhere to pass it: the
  orchestrator's scope comes exclusively from the **`instanceId`** in its binding data
  (case 3). If the starter didn't set `InstanceId = correlationId`, orchestrator logs
  have no scope — there is no header or property to fall back on.

- **Durable activity** — the id goes **inside the activity's input object**, as a
  top-level string property named `CorrelationId` (case-insensitive; case 2). The input
  can be any record with extra fields, but the id must be top-level — nested objects are
  not scanned, and a bare string/int input gives the middleware nothing to inspect. The
  orchestrator's scope does not flow into the activity (separate invocation), so this is
  the id's only ride:

  ```csharp
  // orchestrator side
  await context.CallActivityAsync("MyActivity", new MyInput(correlationId, otherData));

  // activity side — MyInput has a top-level CorrelationId member
  public record MyInput(string CorrelationId, string OtherData);
  ```

  (Case 3 also matches for activities — durable exposes the orchestration's `instanceId`
  in their binding data — so with the instanceId convention in place the scope survives
  even if an input forgets the id.)

The `instanceId = correlationId` convention pulls a lot of weight: one id for the whole
journey, the middleware works inside durable for free, and a duplicate claim event can't
start a second orchestration for the same claim.

## Other things we learned the hard way

- **Isolated-worker double logging / flat traces.** Host and worker both export to OTLP, so
  every log appeared twice and traces had only the host's span. Fix needs all three:
  `"telemetryMode": "OpenTelemetry"` in host.json, `UseFunctionsWorkerDefaults()`, and
  `Microsoft.Azure.Functions.Worker` pinned to 2.52.0.
- **`IncludeScopes = true`** — without it the whole CorrelationId story silently disappears.
- **Dead-letter counting** is done by a trigger on the DLQ itself, not by the failing handler —
  exactly one count per lost claim regardless of retries.
- **Deterministic chaos** ([FailureChaos](src/ClaimFlow.ServiceDefaults/FailureChaos.cs)):
  brick failures are a hash of claim+stage, so a hard-failing claim fails the same way on
  every retry and actually reaches the DLQ instead of succeeding on redelivery.
- **Reuse `ServiceBusSender`s** — one per queue; a sender per message leaks AMQP links.

## Running it

```bash
aspire run
```

Needs Docker for the emulators (Service Bus, Cosmos preview, OTel collector); first cold
start is slow. Start claims from the Dashboard (or hit Comms' HTTP trigger directly), then
watch logs/traces/metrics in the Aspire dashboard.

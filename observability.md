# Observability in the Claims Platform

This page describes how we make the claim-processing pipeline observable: how we follow a single claim across every service that touches it, and how we know the platform itself is healthy.

## Two views of observability

We need to answer two different kinds of question, for two different audiences.

**The business view: "where is this claim, and is the process performing?"**
This is long-lived data about the process itself: the tasks and breadcrumbs WSM stores as a claim progresses, the audit events every system sends to the Audit brick, and any per-claim state other systems keep along the way. It is retained for as long as the business needs it, and it powers a business dashboard: claims in flight, time spent per stage, where claims get stuck.

**The technical view: "is the system healthy, and why did this fail?"**
This is the classic observability triad of logs, distributed traces, and metrics, collected with OpenTelemetry from every service. Its audience is engineers and operations: error rates, latencies, dead-lettered messages, the full trace of one failing request. Most of it is high-volume, short-retention data.

The two views are joined by a single identifier: the **CorrelationId**, minted exactly once when a claim enters the platform. It is the key on the business side (tasks, audit events) and it is stamped on every log line on the technical side. Given a claim, support can find its audit trail; given its CorrelationId, an engineer can pull every log line and the distributed trace for that same claim. How that stamping happens automatically, rather than every developer remembering to do it, is the subject of the rest of this page.

## The technical view

A claim's journey crosses many components: a React front end, web apps, Azure Functions behind HTTP and Service Bus triggers, durable function orchestrations and their activities. Distributed tracing is what stitches one request's path through all of them into a single picture. Each component records its work as a **span**, all spans of one journey share a **trace id**, and that id travels between components in a standard header (W3C Trace Context) on every HTTP call and Service Bus message. The concepts are explained well in [.NET distributed tracing](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts).

Two names come up in this space, and they are not competing technologies:

- **.NET distributed tracing** is the instrumentation built into the platform: the `Activity` API that ASP.NET Core, HttpClient, the Azure SDKs, and our own code use to record spans.
- **OpenTelemetry** is the vendor-neutral standard for collecting and exporting that data. On .NET it does not replace the built-in API, it adopts it: an `Activity` is an OpenTelemetry span, and the OpenTelemetry SDK is what gathers activities and ships them to the monitoring backend.

In short: .NET provides the tracing API, OpenTelemetry provides the pipeline. We instrument once, and the backend is a configuration choice.

Because the glue is the W3C standard rather than anything .NET specific, non-.NET components join the same trace. For the React app this means a trace can start in the browser and continue through every backend hop, as long as the front end sends the `traceparent` header on its API calls (the OpenTelemetry JavaScript SDK and the Application Insights JavaScript SDK both do this; CORS must allow the header, otherwise correlation silently stops at the browser boundary).

### One journey, several traces

Before going into logs and traces separately, it is worth stating the model that drives everything below. A trace covers one bounded operation: a request arrives, work happens, spans close. A claim is not one bounded operation. It can pause for human input and resume hours or days later, and it can pass through hops where trace context cannot follow. When a person acts in the UI to move a claim forward, that action is a new operation with a new trace id; there is no good way (and no good reason) to force it back into the original trace.

So the realistic picture is: **one claim journey is one CorrelationId and several traces**, one per automated leg, split wherever a human step or a context-breaking hop sits in the middle. The two are complementary, not alternatives. Traces give depth within a leg: timing, dependencies, where an error happened. The CorrelationId is the thread across the whole journey and is how the legs are found together. The next two sections cover each half: how the CorrelationId gets onto every log line, and how we keep each trace leg intact.

---

### Logs and the CorrelationId

Distributed traces are valuable, but there are always edge cases where trace context does not survive a hop. Log search is also the tool everyone reaches for first. So our strong recommendation is that **every log line a claim produces, in every service, carries the claim's CorrelationId**, independently of tracing.

The mechanism for this is the **log scope**. A scope attaches key/value pairs to every log line written inside a unit of work, including log lines from services deeper in the call chain that were never given the id. Open the scope once, where the request or message arrives, and everything logged until that work completes carries the CorrelationId automatically. No method signatures change and nobody passes the id to a logger by hand:

```csharp
// at the top of the entry point (HTTP handler, message handler, function)
using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

// every log line from here until the method completes, including in anything it calls
// and across awaits, carries the id
```

One configuration gotcha to be aware of: attaching scopes to log output is opt-in, per logging provider. For us that means `IncludeScopes = true` on the OpenTelemetry logging options:

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
});
```

Without it, scopes are silently dropped and the whole CorrelationId story disappears with no error anywhere.

For the scope to be opened on arrival, the id has to ride along on every hop between services. The general rule: carry it in the transport's **envelope** (headers, properties, metadata), not inside the payload body. Envelope fields can be read uniformly by middleware and by tooling (message peek, dead-letter explorers) without knowing or deserializing each payload's schema. Per transport:

- **HTTP calls**: an `x-correlation-id` request header. Not in the body or query string.
- **Service Bus messages**: a `CorrelationId` application property, not a field in the message body. (Service Bus also has a built-in `CorrelationId` envelope property intended for request/reply correlation; using it instead is fine, but pick one of the two platform-wide.)
- **Event Hubs**: a `CorrelationId` entry in the event's application properties, same rule as Service Bus. One extra care point: consumers typically receive events in batches, so the scope is opened per event inside the loop, not once per batch.
- **Event Grid**: prefer the [CloudEvents schema](https://learn.microsoft.com/en-us/azure/event-grid/cloud-event-schema) and carry the id as an extension attribute (e.g. `correlationid`). The classic EventGridEvent schema is not extensible, so there the only option is a top-level field in `data`; treat that as the fallback, not the pattern.
- **Cosmos change feed**: there is no envelope at all, the document is the message. So every document written as part of claim processing carries a `correlationId` property, and the change-feed consumer opens its scope from that. This hop is also one where trace context cannot flow at all, which is exactly why the CorrelationId, not the trace, is the thread we rely on.
- **Durable orchestrations**: start the orchestration with `InstanceId = CorrelationId`, and include the id in each activity's input.

The id is minted exactly once, at the edge where the claim enters the platform. Everywhere else the rule is propagate, never create: a missing id mid-pipeline is a bug we want to see, not silently paper over with a fresh guid.

Two more things to avoid:

- **Per-team variations of the key name** (`corrId`, `x-request-id`, `correlation_id`). The value of the convention is that one query finds everything; every variation silently splits the search space. One name, everywhere.
- **Using the trace id as the correlation id.** Trace ids are infrastructure: they can be sampled away, restarted at a broken hop, and are not under our control. The CorrelationId is a business identifier we own end to end. The two complement each other, as the next section covers, but they are not interchangeable.

**Open decision: who opens the scope?** There are two ways of working here.

- **A shared middleware.** One reusable component, registered once per service, inspects every incoming invocation (HTTP, Service Bus, durable), finds the CorrelationId, and opens the scope. Consistency is guaranteed by construction, and there is a single place to fix or extend the behaviour. The cost: it is a shared component that someone must own, and it has to handle every trigger type we use correctly.
- **Each team does it themselves.** We publish the conventions above plus code snippets, and every team opens the scope at its own entry points. No shared component to own. The cost: consistency depends on every team's discipline, and one service that forgets breaks the end-to-end view for everyone downstream of it.

To make the middleware option concrete, this is roughly its whole shape. Handling multiple trigger types is not a set of separate code paths, it is one ordered lookup over the invocation's binding data:

```csharp
// registered once per service:  builder.UseMiddleware<CorrelationScopeMiddleware>();
public class CorrelationScopeMiddleware(ILogger<CorrelationScopeMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // one lookup covering every trigger, in order:
        //   1. HTTP        -> the x-correlation-id request header
        //   2. Service Bus -> the CorrelationId application property
        //   3. Durable     -> the activity input's CorrelationId, or the
        //                     orchestration's instanceId (= CorrelationId by convention)
        var correlationId = TryFindCorrelationId(context.BindingContext.BindingData);

        if (correlationId is null)
        {
            await next(context);   // never mint here: a missing id should stay visible
            return;
        }

        using var scope = logger.BeginScope(
            new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        await next(context);
    }
}
```

Every function in the service then gets the scope for free, and the conventions from the previous list live in exactly one place (`TryFindCorrelationId`) instead of in every handler.

A note on overhead: the middleware only reads a few values and opens one scope, which takes microseconds. That is nothing compared to the real work of the function (calling Service Bus, Cosmos, or another service). The one small cost is that every log line now carries the CorrelationId, a few extra bytes per line in the logging bill. That cost is the same with or without the middleware, and it is exactly what we want: it is what makes every log line findable.

---

### Traces

The goal is one trace per claim journey: every hop continues the trace context it received instead of starting a fresh trace. For most of our estate this happens automatically once OpenTelemetry is configured. ASP.NET Core and HttpClient propagate the `traceparent` header on every HTTP call, and the Azure Service Bus SDK stamps it onto every message it sends and links it up on receive. Nobody should hand-roll trace propagation; if two hops do not join into one trace, the fix is in configuration, not in passing ids around.

Where we stand per component:

**Web apps and APIs.** Trace propagation over HTTP is the most mature part of the .NET OpenTelemetry story and works out of the box. We treat this as solid.

**Function apps.** HTTP and Service Bus triggers we have validated: the context flows and the hops join into one trace. The other triggers we use (Event Hubs, Event Grid) carry the same standard context and are expected to behave the same, but each should be verified before we rely on it. One overlay to keep in mind: OpenTelemetry output from isolated-worker function apps is still marked preview by Microsoft, so keeping the Functions packages current matters here more than usual.

**Durable Functions.** Whether the orchestration, its activities, and sub-orchestrations join into one trace tree depends on the Durable extension version and configuration: recent versions support distributed tracing, but it has to be switched on ([how-to and details](https://learn.microsoft.com/en-us/azure/durable-task/sdks/durable-task-scheduler-opentelemetry-tracing?tabs=csharp&pivots=durable-functions)):

```json
"extensions": {
  "durableTask": {
    "tracing": { "DistributedTracingEnabled": true, "Version": "V2" }
  }
}
```

plus registering the `Microsoft.DurableTask` activity source with OpenTelemetry. The first step for us is to confirm the extension versions our apps actually run and verify the trace tree end to end against our orchestration backend.

**Cosmos change feed.** A document in a container has no envelope, so trace context cannot ride through this hop: the change-feed consumer starts a new trace. That is structural, not a version issue, so the plan is to accept the break rather than work around it; the CorrelationId on the document (previous section) is the thread that survives and is how the two trace fragments are found again. The exact behaviour with our SDK versions is still to be verified.

**The React front end.** A trace ideally starts in the browser, so the user's action and every backend hop it caused form one picture. The mechanism is the front end sending the `traceparent` header on its API calls (both the OpenTelemetry JS SDK and the Application Insights JS SDK can do this), and the classic failure mode is CORS not allowing the header, which breaks correlation silently. We still need to validate this end to end in our setup. If the front end does not participate, the consequence is mild: the trace simply starts at the first API instead of the browser.

In short, the pieces we have not validated yet: Event Hubs and Event Grid trigger correlation, the Durable extension versions and configuration in our apps, change-feed behaviour with our SDK versions, and browser-to-backend correlation including CORS.

One more silent trace-breaker to know about: **sampling**. Application Insights samples telemetry by default, and a sampled-out span makes a trace look broken even though propagation worked fine. When traces come up short in production, check sampling settings before suspecting the plumbing.

**Do we need to store the trace id against the CorrelationId?** Our recommendation: no separate store is needed, because the logs already are that mapping. Every log line written inside a trace carries the trace id automatically, and with the scope from the previous section it carries the CorrelationId too. So one query by CorrelationId returns the trace ids of every fragment of the journey, including across the breaks described above. The practical rule that keeps this true: every service logs at least one line per unit of work.

---

### Metrics

Metrics are the lightest of the three signals, and unlike the CorrelationId they are a plus rather than a foundation: nothing breaks without them. What they buy is an always-on early warning. Logs and traces answer "what happened to this claim"; metrics answer "is anything wrong right now" at a glance, without querying any database.

The recommendation is to treat each stage of the process as a **checkpoint** that emits a few counters via an OpenTelemetry meter: claims received, processed, failed, plus a processing-time histogram. This costs a handful of lines per service, and it works the same for services that own a database and for the atomic ones that keep no state at all; the meter is their only footprint, and it is enough.

Per-stage counters make two cheap checks possible:

- **Reconciliation.** For any stage, received should equal processed plus failed plus dead-lettered once things settle. When the numbers do not add up, a claim was lost silently, which is precisely the failure mode nothing else surfaces: no error was logged, because nothing knew to log one. The same idea works across stages (stage N sent 100, stage N+1 received 98) and against the task store.
- **Dead-letter visibility.** A message landing on a dead-letter queue becomes a counter increment, so "we lost one" is a dashboard signal and a potential alert, not something discovered in a queue browser weeks later. One subtlety: count on arrival at the dead-letter queue itself, not in the failing handler, so retries of the same message do not inflate the number.

For these checks to work, one thing has to be defined up front: **how retries are counted**. Service Bus (and the other messaging services) redeliver a message after a failure, so a stage that counts "received" on every delivery attempt but "processed" once per claim drifts a little further on every retry, and the mismatch looks exactly like a lost claim. The rule we propose: the reconciliation counters count claims, not attempts. Received increments on first delivery only (the message's delivery count tells you whether this is a retry), and failed increments on the final outcome, not per failed attempt. If retry activity is worth watching, and it often is an early sign of a struggling dependency, give it its own counter instead of bending the reconciliation ones.

On top of these, a **simple dashboard**: one row of tiles per stage showing throughput, failures, and dead-letters. Its job is a ten-second scan, either everything is flowing or one stage is off. From there the investigation moves to logs: the dashboard gives the stage and the time window, the logs filtered on those give the failing claims' CorrelationIds, and each CorrelationId gives the full journey.

One deliberate design choice makes that hand-off necessary: **metrics stay anonymous**. It is tempting to tag a failure counter with the CorrelationId, but every distinct value of a metric dimension becomes its own time series, and per-claim dimensions make the number of series explode, which metric backends charge for and eventually reject. Metrics locate the problem; identifying the individual claim is the job of the logs.


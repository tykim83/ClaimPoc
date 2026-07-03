# Fixing double logs (and flat traces) in .NET isolated Azure Functions + OpenTelemetry

## Symptom

Every user log line shows up **twice** in the telemetry backend (Application Insights,
or any OTLP collector), and the distributed trace is **flat** — you only see the host's
inbound HTTP span, not the spans for the work your function actually did.

If you've ever stared at App Insights wondering why one `_logger.LogInformation(...)`
produced two identical rows, this is it. It is the same root cause as the classic
App Insights double-logging in isolated Functions.

## Root cause: two processes, both exporting

The .NET **isolated** worker model runs your function in a **separate process** from the
Functions **host**:

```
  ┌─────────────┐   gRPC    ┌──────────────┐
  │ Functions   │ ───────►  │  Your worker │
  │ host        │           │  process     │
  │ (in-proc    │ ◄───────  │  (your code, │
  │  runtime)   │           │   ILogger)   │
  └─────────────┘           └──────────────┘
        │                          │
        └────────► OTLP / App Insights ◄────────┘
             BOTH export telemetry
```

Out of the box **both** processes export to the same telemetry endpoint:

- The **worker** emits your log with its full scope (e.g. the `CorrelationId` scope).
- The **host** *also* relays a **stripped copy** of that same log (no scope).

→ Two rows per log line. And because the host owns the inbound HTTP `Activity` but the
worker's `ActivitySource` isn't wired into the same pipeline, the worker's spans never
attach to the host's trace → the trace looks flat.

## The fix — three parts, all required

Missing any one of the three either leaves the duplication in place or throws at runtime.

### 1. `host.json` — hand telemetry to OpenTelemetry, drop the App Insights logging block

```jsonc
{
    "version": "2.0",
    "telemetryMode": "OpenTelemetry"
}
```

`telemetryMode: "OpenTelemetry"` tells the **host** to stop relaying its own stripped copy
of worker logs and defer to the worker's OpenTelemetry pipeline. Also remove the old
`logging.applicationInsights` block if present — that's the legacy path that produces the
second copy.

**Before** (produces duplicates):

```jsonc
{
    "version": "2.0",
    "logging": {
        "applicationInsights": {
            "samplingSettings": { "isEnabled": true, "excludedTypes": "Request" },
            "enableLiveMetricsFilters": true
        }
    }
}
```

### 2. `Program.cs` — register the worker's OpenTelemetry integration

```csharp
using Microsoft.Azure.Functions.Worker.OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults(); // your OTel setup (exporters, resource, etc.)

// Integrate the isolated worker's telemetry with the Functions host pipeline:
// registers the worker ActivitySource (function/service work -> child spans in the
// host trace) and coordinates logging so the host stops relaying a duplicate copy
// of each user log.
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

builder.Build().Run();
```

`UseFunctionsWorkerDefaults()` registers the worker's `ActivitySource` so your function
work becomes **child spans** of the host's HTTP span (one grouped trace), and coordinates
logging so the worker is the single source of each log line.

> Note the namespace: `UseFunctionsWorkerDefaults` lives in
> `Microsoft.Azure.Functions.Worker.OpenTelemetry` — if the `using` isn't obvious, that's
> the package/namespace to import.

### 3. Align the Worker packages (or you get a runtime crash)

Add `Microsoft.Azure.Functions.Worker.OpenTelemetry` and bump the worker packages so
`Worker` / `Worker.Core` / `Worker.Grpc` are on the **same** version. In this repo that
version is **2.52.0**:

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.52.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.OpenTelemetry" Version="1.2.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.0.5" />
```

If the worker packages are misaligned (e.g. `Worker` left at an older 2.x while the
OpenTelemetry package expects 2.52.0), the app throws at startup with a
**`MissingMethodException` on `DefaultTraceContext..ctor`** and every request returns 500.
The OpenTelemetry package calls a constructor that only exists in the aligned version, so
this is a hard version-coupling, not just a "nice to have."

## Before / after in this repo

The fix landed in commit `fb69db1` ("fix logger"). The three changes were exactly the
three parts above:

| File | Before | After |
|---|---|---|
| `host.json` | `logging.applicationInsights { … }` | `"telemetryMode": "OpenTelemetry"` |
| `Program.cs` | *(nothing)* | `AddOpenTelemetry().UseFunctionsWorkerDefaults()` |
| `*.csproj` | `Worker` 2.1.0, no OTel pkg | `Worker` 2.52.0 + `Worker.OpenTelemetry` 1.2.0 |

**Result:** each user log appears **once** (with its `CorrelationId` scope intact), and
there is **one grouped trace** (inbound HTTP → your function span → downstream spans).

## How to verify

1. Fire one request that logs a known line.
2. In the backend (App Insights / your OTLP dashboard) confirm that line appears **once**,
   not twice, and that it carries your log **scope** (e.g. `CorrelationId`).
3. Open the trace and confirm the function work shows as **child spans** under the inbound
   request, not a single flat HTTP span.

If you still see two copies, the usual culprit is a leftover `applicationInsights` logging
block in `host.json` (part 1), or a second `AddApplicationInsightsTelemetry*` registration
somewhere adding its own logger provider.

## Applying this at scale

- Every isolated-worker Function project needs **all three** parts — they don't inherit
  from a shared project (host.json and Program.cs are per-app). When you add a new
  function app, copy all three or you reintroduce the duplication in just that app.
- Keep the worker package versions pinned **together** whenever you bump any of them, to
  avoid the `DefaultTraceContext` crash.
- This is a **logs/traces** concern only. It does not affect custom **metrics** you emit
  via your own `Meter`/`IMeterFactory` — those go straight through OTel and are not
  relayed by the host, so they were never doubled.

# Double logs in .NET isolated Azure Functions + OpenTelemetry

## Symptom

Every log line appears **twice** in the backend (App Insights or any OTLP collector),
and the trace is **flat**: only the host's HTTP span, no spans for your actual work.

## Cause

The isolated model runs your code in a **separate process** from the Functions host,
and by default **both** processes export telemetry:

- the **worker** emits your log, with its scope (e.g. `CorrelationId`)
- the **host** relays a second, stripped copy of the same log

Two rows per log line. And since the worker's spans aren't wired into the host's
trace, the trace stays flat.

## Fix — three parts, all required

**1. `host.json`** — tell the host to defer to the worker. Delete any old
`logging.applicationInsights` block, that's where the second copy comes from:

```json
{
    "version": "2.0",
    "telemetryMode": "OpenTelemetry"
}
```

**2. `Program.cs`** — register the worker-side integration
(namespace `Microsoft.Azure.Functions.Worker.OpenTelemetry`):

```csharp
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();
```

**3. `.csproj`** — add the OTel package and align the worker packages
(`Worker` / `Worker.Core` / `Worker.Grpc` must be the **same** version):

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.52.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.OpenTelemetry" Version="1.2.0" />
```

If the versions are misaligned the app crashes at startup with
`MissingMethodException: DefaultTraceContext..ctor` and every request 500s.

## Verify

Fire one request, then check:

1. the log line appears **once**, with its scope (`CorrelationId`) intact
2. the trace shows your function work as **child spans** under the inbound request

Still doubled? Look for a leftover `applicationInsights` block in `host.json` or a
stray `AddApplicationInsightsTelemetry*` call registering its own logger provider.

## Gotchas

- `host.json` and `Program.cs` are **per function app** — every new app needs all
  three parts, or that app doubles its logs again.
- Bump the worker packages **together**, never individually.
- Custom metrics were never doubled; the host only relays logs/traces.

In this repo the fix landed in commit `fb69db1`.

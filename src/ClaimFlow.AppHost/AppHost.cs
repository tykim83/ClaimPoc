var builder = DistributedApplication.CreateBuilder(args);

// Service Bus emulator (brings a SQL Server sidecar; first cold start is slow).
// Persistent lifetime avoids re-pulling the images every run.
var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator(emulator => emulator.WithLifetime(ContainerLifetime.Persistent));

// Comms -> Tasks queue.
var orchestratorIn = serviceBus.AddServiceBusQueue("orchestrator-in");

// Tasks -> each brick, and every brick -> Tasks (shared responses queue).
var classifierIn = serviceBus.AddServiceBusQueue("classifier-in");
var preparerIn = serviceBus.AddServiceBusQueue("preparer-in");
var filerIn = serviceBus.AddServiceBusQueue("filer-in");
serviceBus.AddServiceBusQueue("orchestrator-responses");

// Cosmos preview emulator: append-only event log, partition key = /correlationId.
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(emulator => emulator
        .WithDataExplorer()
        .WithLifetime(ContainerLifetime.Persistent));
var events = cosmos.AddCosmosDatabase("claimflow").AddContainer("events", "/correlationId");

// OTel Collector: services push their metrics here via OTLP; it re-exposes them as
// Prometheus at :8889 for the dashboard to scrape. Traces/logs still go straight to
// the Aspire dashboard — this is an additional metrics-only fan-out.
var collector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "latest")
    .WithBindMount("collector-config.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithEndpoint(targetPort: 4317, name: "otlp-grpc", scheme: "http")
    .WithEndpoint(targetPort: 8889, name: "prometheus", scheme: "http")
    .WithLifetime(ContainerLifetime.Persistent);

var collectorOtlp = collector.GetEndpoint("otlp-grpc");

var comms = builder.AddAzureFunctionsProject<Projects.ClaimFlow_Comms>("s1-comms")
    .WithReference(serviceBus)
    .WithEnvironment("COLLECTOR_OTLP_ENDPOINT", collectorOtlp)
    .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "2000")
    .WaitFor(orchestratorIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Tasks>("s2-tasks")
    .WithReference(serviceBus)
    .WithReference(cosmos)
    .WithEnvironment("COLLECTOR_OTLP_ENDPOINT", collectorOtlp)
    .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "2000")
    .WaitFor(orchestratorIn)
    .WaitFor(events);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Classifier>("s3-classifier")
    .WithReference(serviceBus)
    .WithEnvironment("COLLECTOR_OTLP_ENDPOINT", collectorOtlp)
    .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "2000")
    .WaitFor(classifierIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Preparer>("s4-preparer")
    .WithReference(serviceBus)
    .WithEnvironment("COLLECTOR_OTLP_ENDPOINT", collectorOtlp)
    .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "2000")
    .WaitFor(preparerIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Filer>("s5-filer")
    .WithReference(serviceBus)
    .WithEnvironment("COLLECTOR_OTLP_ENDPOINT", collectorOtlp)
    .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "2000")
    .WaitFor(filerIn);

// Blazor dashboard: starts runs (via the Comms HTTP trigger), reads the Cosmos
// event log, and scrapes the collector's Prometheus endpoint for the metrics charts.
builder.AddProject<Projects.ClaimFlow_Dashboard>("s6-dashboard")
    .WithReference(comms)
    .WithReference(cosmos)
    .WithEnvironment("COLLECTOR_METRICS_ENDPOINT", collector.GetEndpoint("prometheus"))
    .WithExternalHttpEndpoints()
    .WaitFor(comms)
    .WaitFor(events);

builder.Build().Run();

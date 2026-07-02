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

var comms = builder.AddAzureFunctionsProject<Projects.ClaimFlow_Comms>("s1-comms")
    .WithReference(serviceBus)
    .WaitFor(orchestratorIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Tasks>("s2-tasks")
    .WithReference(serviceBus)
    .WithReference(cosmos)
    .WaitFor(orchestratorIn)
    .WaitFor(events);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Classifier>("s3-classifier")
    .WithReference(serviceBus)
    .WaitFor(classifierIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Preparer>("s4-preparer")
    .WithReference(serviceBus)
    .WaitFor(preparerIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Filer>("s5-filer")
    .WithReference(serviceBus)
    .WaitFor(filerIn);

// Blazor dashboard: starts runs (via the Comms HTTP trigger), reads the Cosmos
// event log, charts metrics.
builder.AddProject<Projects.ClaimFlow_Dashboard>("s6-dashboard")
    .WithReference(comms)
    .WithReference(cosmos)
    .WithExternalHttpEndpoints()
    .WaitFor(comms)
    .WaitFor(events);

builder.Build().Run();

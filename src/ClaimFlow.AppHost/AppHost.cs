var builder = DistributedApplication.CreateBuilder(args);

// Service Bus emulator (brings a SQL Server sidecar; first cold start is slow).
// Persistent lifetime avoids re-pulling the images every run.
var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator(emulator => emulator.WithLifetime(ContainerLifetime.Persistent));

// Comms -> Tasks queue.
var orchestratorIn = serviceBus.AddServiceBusQueue("orchestrator-in");

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Comms>("s1-comms")
    .WithReference(serviceBus)
    .WaitFor(orchestratorIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Tasks>("s2-tasks")
    .WithReference(serviceBus)
    .WaitFor(orchestratorIn);

builder.Build().Run();

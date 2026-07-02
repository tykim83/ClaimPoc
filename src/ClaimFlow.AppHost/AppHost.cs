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

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Comms>("s1-comms")
    .WithReference(serviceBus)
    .WaitFor(orchestratorIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Tasks>("s2-tasks")
    .WithReference(serviceBus)
    .WaitFor(orchestratorIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Classifier>("s3-classifier")
    .WithReference(serviceBus)
    .WaitFor(classifierIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Preparer>("s4-preparer")
    .WithReference(serviceBus)
    .WaitFor(preparerIn);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Filer>("s5-filer")
    .WithReference(serviceBus)
    .WaitFor(filerIn);

builder.Build().Run();

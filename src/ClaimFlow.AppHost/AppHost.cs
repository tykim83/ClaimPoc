var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Comms>("s1-comms");

builder.Build().Run();

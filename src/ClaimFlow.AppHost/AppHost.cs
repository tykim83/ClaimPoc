var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureFunctionsProject<Projects.ClaimFlow_Intake>("s1-intake");

builder.Build().Run();

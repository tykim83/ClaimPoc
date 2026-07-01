using ClaimFlow.Intake;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IIntakeService, IntakeService>();

builder.Build().Run();

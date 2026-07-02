using ClaimFlow.Filer;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Aspire Service Bus client (publisher for the response back to Tasks).
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

builder.Services.AddSingleton<IFilerService, FilerService>();

builder.UseMiddleware<CorrelationScopeMiddleware>();

builder.Build().Run();

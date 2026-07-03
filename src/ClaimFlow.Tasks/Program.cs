using ClaimFlow.ServiceDefaults;
using ClaimFlow.Tasks;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddAzureServiceBusClient("messaging");

// event log store (partition key = /correlationId)
builder.AddAzureCosmosClient("cosmos");

builder.Services.AddSingleton<ServiceBusSenderCache>();

// without this the host relays a second copy of every log and the trace stays flat,
// see docs/isolated-worker-double-logging.md
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

// opens the CorrelationId log scope for every triggered function
builder.UseMiddleware<CorrelationScopeMiddleware>();

builder.Build().Run();

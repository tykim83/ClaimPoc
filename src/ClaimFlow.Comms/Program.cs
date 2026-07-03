using ClaimFlow.Comms;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddAzureServiceBusClient("messaging");

// without this the host relays a second copy of every log and the trace stays flat,
// see docs/isolated-worker-double-logging.md
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

builder.Services.AddSingleton<ICommsService, CommsService>();

builder.Build().Run();

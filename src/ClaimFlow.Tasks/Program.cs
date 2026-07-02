using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Publisher used by the fan-out activity to send requests to the bricks.
builder.AddAzureServiceBusClient("messaging");

// Cosmos client for the append-only event log (partition key = /correlationId).
builder.AddAzureCosmosClient("cosmos");

// Integrate the isolated worker's telemetry with the Functions host pipeline:
// registers the worker ActivitySource (function/service work -> child spans in
// the host trace) and coordinates logging so the host stops relaying a duplicate
// copy of each user log. Same reasoning as ClaimFlow.Comms.
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

// Opens the CorrelationId log scope automatically for every triggered function.
builder.UseMiddleware<CorrelationScopeMiddleware>();

builder.Build().Run();

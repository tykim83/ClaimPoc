using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.ServiceDefaults;

// Opens a CorrelationId log scope around every Service Bus-triggered function, so entry
// points don't repeat the BeginScope boilerplate. The trigger exposes the message's
// application properties as a JSON string in the binding data; we read our key from there.
public class CorrelationScopeMiddleware(ILogger<CorrelationScopeMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private const string CorrelationIdKey = "CorrelationId";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var correlationId = TryFindCorrelationId(context);
        if (correlationId is null)
        {
            await next(context);
            return;
        }

        using (logger.BeginScope(new Dictionary<string, object> { [CorrelationIdKey] = correlationId }))
        {
            await next(context);
        }
    }

    private static string? TryFindCorrelationId(FunctionContext context)
    {
        if (!context.BindingContext.BindingData.TryGetValue("ApplicationProperties", out var raw)
            || raw is not string json)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(CorrelationIdKey, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

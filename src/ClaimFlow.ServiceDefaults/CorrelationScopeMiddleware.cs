using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.ServiceDefaults;

// Opens a CorrelationId log scope around every function invocation that carries one,
// so entry points don't repeat the BeginScope boilerplate. Service Bus triggers carry
// the id in the message's application properties; activity triggers carry it inside
// their serialized input. Both surface in the binding data as JSON strings, so we
// scan any JSON object there for a top-level CorrelationId.
//
// Durable triggers also expose the instanceId, and by convention orchestrations are
// started with instanceId = correlationId — so when no JSON carries the id (the
// orchestrator itself, activities with plain inputs), the instanceId is the fallback.
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
        foreach (var value in context.BindingContext.BindingData.Values)
        {
            if (value is not string s || !s.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    // case-insensitive: the durable serializer may camelCase the input
                    if (prop.Value.ValueKind == JsonValueKind.String
                        && string.Equals(prop.Name, CorrelationIdKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // a string that merely looks like JSON, skip it
            }
        }

        // durable fallback: instanceId = correlationId by convention
        foreach (var kv in context.BindingContext.BindingData)
        {
            if (string.Equals(kv.Key, "instanceId", StringComparison.OrdinalIgnoreCase)
                && kv.Value is string instanceId && instanceId.Length > 0)
            {
                return instanceId;
            }
        }

        return null;
    }
}

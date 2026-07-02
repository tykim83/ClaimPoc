using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.ServiceDefaults;

// Opens a CorrelationId log scope for every function invocation where a correlationId can
// be found on the trigger — no matter the trigger type — so entry points don't each repeat
// the BeginScope boilerplate. The traceId is added by the runtime automatically, so it is
// not added here. Tolerates common spellings (CorrelationId, correlation-id, x-correlation-id…).
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
        var data = context.BindingContext.BindingData;

        // Service Bus triggers expose the message's app properties as an
        // "ApplicationProperties" JSON string in the trigger metadata.
        if (data.TryGetValue("ApplicationProperties", out var raw) && raw is string json)
        {
            var fromProps = FindInJson(json);
            if (fromProps is not null)
            {
                return fromProps;
            }
        }

        // Fallback: any top-level trigger metadata key spelled like a correlation id.
        foreach (var kv in data)
        {
            if (IsCorrelationId(kv.Key) && kv.Value is string s && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        return null;
    }

    private static string? FindInJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (IsCorrelationId(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON we can read — ignore.
        }

        return null;
    }

    // Normalize away case and separators: CorrelationId, correlation-id, correlation_id,
    // "Correlation Id", x-correlation-id, X-Correlation-ID all match.
    private static bool IsCorrelationId(string name)
    {
        var normalized = new string([.. name.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
        return normalized is "correlationid" or "xcorrelationid";
    }
}

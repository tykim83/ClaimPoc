using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.ServiceDefaults;

// Opens a CorrelationId log scope around every function invocation, whatever the
// trigger. Resolution order:
//   1. x-correlation-id (or CorrelationId) HTTP request header
//   2. any JSON object in the binding data with a top-level CorrelationId
//      (Service Bus application properties, durable activity inputs)
//   3. the durable instanceId (orchestrations are started with instanceId = correlationId)
// It never creates an id: if nothing is found the invocation just runs without the
// scope. Minting belongs to the system edge (the HTTP entry function), not here -
// mid-pipeline a missing id is a bug to surface, not to paper over.
// A found id is also stored in context.Items so entry points can echo it back.
public class CorrelationScopeMiddleware(ILogger<CorrelationScopeMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public const string ItemKey = "CorrelationId";
    private const string HeaderName = "x-correlation-id";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var correlationId = TryFindCorrelationId(context);
        if (correlationId is null)
        {
            await next(context);
            return;
        }

        context.Items[ItemKey] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await next(context);
        }
    }

    private static string? TryFindCorrelationId(FunctionContext context)
    {
        var bindingData = context.BindingContext.BindingData;

        // explicit header first
        if (bindingData.TryGetValue("Headers", out var headersRaw) && headersRaw is string headersJson)
        {
            var fromHeader = FindInJsonObject(headersJson, HeaderName) ?? FindInJsonObject(headersJson, ItemKey);
            if (fromHeader is not null)
            {
                return fromHeader;
            }
        }

        // any JSON value carrying a top-level CorrelationId
        foreach (var value in bindingData.Values)
        {
            if (value is string s && s.StartsWith('{') && FindInJsonObject(s, ItemKey) is { } found)
            {
                return found;
            }
        }

        // durable fallback: instanceId = correlationId by convention
        foreach (var kv in bindingData)
        {
            if (string.Equals(kv.Key, "instanceId", StringComparison.OrdinalIgnoreCase)
                && kv.Value is string instanceId && instanceId.Length > 0)
            {
                return instanceId;
            }
        }

        return null;
    }

    private static string? FindInJsonObject(string json, string propertyName)
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
                // case-insensitive: serializers and clients disagree on casing
                if (prop.Value.ValueKind == JsonValueKind.String
                    && string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                {
                    return prop.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // a string that merely looks like JSON, skip it
        }

        return null;
    }
}

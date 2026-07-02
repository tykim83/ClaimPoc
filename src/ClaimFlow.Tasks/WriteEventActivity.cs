using System.Diagnostics;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

// What the orchestrator passes. id + timestamp are NOT set here — they're generated in
// the activity, since Guid/clock are non-deterministic and must stay out of the orchestrator.
public record WriteEventInput(string EventType, string CorrelationId, string OrchestratorId, string? Stage, string Status);

// What actually gets stored (one append-only doc per event). PartitionKey = correlationId.
// Every event carries a status: "Success" for the normal flow, and Success/Failed on the
// single terminal "process-completed" event.
public record EventRecord(
    string id,
    string correlationId,
    string eventType,
    string status,
    string? stage,
    string orchestratorId,
    string? traceId,
    DateTime timestamp);

// Activity: the only place that writes to Cosmos. Re-opens the CorrelationId scope from
// its input (activities are separate invocations).
public class WriteEventActivity(CosmosClient cosmosClient, ILogger<WriteEventActivity> logger)
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string DatabaseName = "claimflow";
    private const string ContainerName = "events";

    [Function(nameof(WriteEventActivity))]
    public async Task Run([ActivityTrigger] WriteEventInput input)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = input.CorrelationId,
        });

        var record = new EventRecord(
            id: Guid.NewGuid().ToString("N"),
            correlationId: input.CorrelationId,
            eventType: input.EventType,
            status: input.Status,
            stage: input.Stage,
            orchestratorId: input.OrchestratorId,
            traceId: Activity.Current?.TraceId.ToString(),
            timestamp: DateTime.UtcNow);

        var container = cosmosClient.GetContainer(DatabaseName, ContainerName);
        await container.CreateItemAsync(record, new PartitionKey(input.CorrelationId));

        logger.LogInformation("S2-Tasks: stored event {EventType}", input.EventType);
    }
}

using System.Diagnostics;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

// id + timestamp are generated in the activity, not here: Guid/clock are
// non-deterministic and must stay out of the orchestrator.
public record WriteEventInput(string EventType, string CorrelationId, string? Stage, string Status);

// One append-only doc per event, partitioned by correlationId.
public record EventRecord(
    string id,
    string correlationId,
    string eventType,
    string status,
    string? stage,
    string? traceId,
    DateTime timestamp);

// The only place that writes to Cosmos. The CorrelationId scope comes from the
// middleware, which finds the id inside the activity's input.
public class WriteEventActivity(CosmosClient cosmosClient, ILogger<WriteEventActivity> logger)
{
    private const string DatabaseName = "claimflow";
    private const string ContainerName = "events";

    [Function(nameof(WriteEventActivity))]
    public async Task Run([ActivityTrigger] WriteEventInput input)
    {
        var record = new EventRecord(
            id: Guid.NewGuid().ToString("N"),
            correlationId: input.CorrelationId,
            eventType: input.EventType,
            status: input.Status,
            stage: input.Stage,
            traceId: Activity.Current?.TraceId.ToString(),
            timestamp: DateTime.UtcNow);

        var container = cosmosClient.GetContainer(DatabaseName, ContainerName);
        await container.CreateItemAsync(record, new PartitionKey(input.CorrelationId));

        logger.LogInformation("S2-Tasks: stored event {EventType}", input.EventType);
    }
}

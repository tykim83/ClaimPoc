using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

public record BrickRequest(string Stage, string CorrelationId, string OrchestratorId);

public class ClaimOrchestrator
{
    private const string CorrelationIdKey = "CorrelationId";
    private static readonly string[] Stages = ["Classifier", "Preparer", "Filer"];

    [Function(nameof(ClaimOrchestrator))]
    public async Task Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var correlationId = context.GetInput<string>()!;
        var logger = context.CreateReplaySafeLogger<ClaimOrchestrator>();

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = correlationId,
        });

        logger.LogInformation("S2-Tasks Orchestrator: started for claim");
        await WriteEvent(context, "orchestration-started", correlationId, null);

        // Sequential calls to each brick: record the request, send it (via the activity —
        // the only place I/O is allowed), then wait for the matching response event.
        foreach (var stage in Stages)
        {
            await WriteEvent(context, $"{stage.ToLowerInvariant()}-requested", correlationId, stage);

            await context.CallActivityAsync(
                nameof(SendToBrickActivity),
                new BrickRequest(stage, correlationId, context.InstanceId));

            var status = await context.WaitForExternalEvent<string>($"{stage}Done");
            if (status == "Failed")
            {
                // A stage soft-failed: stop here and close the claim with a failed terminal event.
                await WriteEvent(context, "process-completed", correlationId, stage, "Failed");
                logger.LogWarning("S2-Tasks Orchestrator: {Stage} failed, claim marked failed", stage);
                return;
            }

            await WriteEvent(context, $"{stage.ToLowerInvariant()}-done", correlationId, stage);
            logger.LogInformation("S2-Tasks Orchestrator: {Stage} completed", stage);
        }

        await WriteEvent(context, "process-completed", correlationId, null, "Success");
        logger.LogInformation("S2-Tasks Orchestrator: all stages done");
    }

    // Every event is "Success" except the terminal process-completed event, which
    // carries Success or Failed.
    private static Task WriteEvent(TaskOrchestrationContext context, string eventType, string correlationId, string? stage, string status = "Success")
    {
        return context.CallActivityAsync(
            nameof(WriteEventActivity),
            new WriteEventInput(eventType, correlationId, context.InstanceId, stage, status));
    }
}

// Activity: the only place that does I/O (publishes the brick request). It re-opens the
// CorrelationId scope from its input, since activities are separate invocations.
public class SendToBrickActivity(ServiceBusClient serviceBusClient, ILogger<SendToBrickActivity> logger)
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";

    [Function(nameof(SendToBrickActivity))]
    public async Task Run([ActivityTrigger] BrickRequest req)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = req.CorrelationId,
        });

        var queue = $"{req.Stage.ToLowerInvariant()}-in";
        var sender = serviceBusClient.CreateSender(queue);
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { claimId = req.CorrelationId }))
        {
            ApplicationProperties =
            {
                [CorrelationIdKey] = req.CorrelationId,
                [OrchestratorIdKey] = req.OrchestratorId,
            },
        };
        await sender.SendMessageAsync(message);

        logger.LogInformation("S2-Tasks: sent request to {Queue}", queue);
    }
}

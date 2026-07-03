using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

public record BrickRequest(string Stage, string CorrelationId);

public class ClaimOrchestrator
{
    private static readonly string[] Stages = ["Classifier", "Preparer", "Filer"];

    [Function(nameof(ClaimOrchestrator))]
    public async Task Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        // instanceId IS the correlationId (the starter sets it), so the middleware
        // opens the log scope here too — no BeginScope even in the orchestrator.
        var correlationId = context.InstanceId;
        var logger = context.CreateReplaySafeLogger<ClaimOrchestrator>();

        logger.LogInformation("S2-Tasks Orchestrator: started for claim");
        await WriteEvent(context, "orchestration-started", correlationId, null);

        // one brick at a time: record, send (activities do the I/O), wait for the reply
        foreach (var stage in Stages)
        {
            await WriteEvent(context, $"{stage.ToLowerInvariant()}-requested", correlationId, stage);

            await context.CallActivityAsync(
                nameof(SendToBrickActivity),
                new BrickRequest(stage, correlationId));

            var status = await context.WaitForExternalEvent<string>($"{stage}Done");
            if (status == "Failed")
            {
                // A stage soft-failed: stop here and close the claim with a failed terminal event.
                await WriteEvent(context, "process-completed", correlationId, stage, "Failed");
                await context.CallActivityAsync(nameof(RecordOutcomeActivity), "Failed");
                logger.LogWarning("S2-Tasks Orchestrator: {Stage} failed, claim marked failed", stage);
                return;
            }

            await WriteEvent(context, $"{stage.ToLowerInvariant()}-done", correlationId, stage);
            logger.LogInformation("S2-Tasks Orchestrator: {Stage} completed", stage);
        }

        await WriteEvent(context, "process-completed", correlationId, null, "Success");
        await context.CallActivityAsync(nameof(RecordOutcomeActivity), "Success");
        logger.LogInformation("S2-Tasks Orchestrator: all stages done");
    }

    // Every event is "Success" except the terminal process-completed event, which
    // carries Success or Failed.
    private static Task WriteEvent(TaskOrchestrationContext context, string eventType, string correlationId, string? stage, string status = "Success")
    {
        return context.CallActivityAsync(
            nameof(WriteEventActivity),
            new WriteEventInput(eventType, correlationId, stage, status));
    }
}

// Publishes the brick request. The CorrelationId scope comes from the middleware,
// which finds the id inside the activity's input.
public class SendToBrickActivity(ServiceBusSenderCache senders, ILogger<SendToBrickActivity> logger)
{
    private const string CorrelationIdKey = "CorrelationId";

    [Function(nameof(SendToBrickActivity))]
    public async Task Run([ActivityTrigger] BrickRequest req)
    {
        var queue = $"{req.Stage.ToLowerInvariant()}-in";
        var sender = senders.Get(queue);
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { claimId = req.CorrelationId }))
        {
            ApplicationProperties =
            {
                [CorrelationIdKey] = req.CorrelationId,
            },
        };
        await sender.SendMessageAsync(message);

        logger.LogInformation("S2-Tasks: sent request to {Queue}", queue);
    }
}

// Counts the terminal outcome. Lives in an activity because the orchestrator body
// replays; an activity runs exactly once.
public class RecordOutcomeActivity(ClaimIntakeMetrics metrics)
{
    [Function(nameof(RecordOutcomeActivity))]
    public void Run([ActivityTrigger] string status)
    {
        if (status == "Failed")
        {
            metrics.S2TasksFailed.Add(1);
        }
        else
        {
            metrics.S2TasksProcessed.Add(1);
        }
    }
}

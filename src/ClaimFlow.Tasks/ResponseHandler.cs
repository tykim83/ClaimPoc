using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

// Receives brick replies on the shared responses queue and resumes the right
// orchestration. OrchestratorId routes the event; Stage names it; CorrelationId
// is only for the log scope.
public class ResponseHandler
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string StageKey = "Stage";
    private const string QueueName = "orchestrator-responses";

    [Function(nameof(ResponseHandler))]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = "messaging")] ServiceBusReceivedMessage message,
        [DurableClient] DurableTaskClient durableClient,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<ResponseHandler>();

        message.ApplicationProperties.TryGetValue(CorrelationIdKey, out var c);
        message.ApplicationProperties.TryGetValue(OrchestratorIdKey, out var o);
        message.ApplicationProperties.TryGetValue(StageKey, out var s);
        var correlationId = c as string;
        var orchestratorId = o as string;
        var stage = s as string;
        if (correlationId is null || orchestratorId is null || stage is null)
        {
            logger.LogWarning("S2-Tasks response handler: missing routing properties, ignoring");
            return;
        }

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = correlationId,
        });

        logger.LogInformation("S2-Tasks response handler: {Stage} replied, resuming orchestration", stage);

        await durableClient.RaiseEventAsync(orchestratorId, $"{stage}Done", "ok", ct);
    }
}

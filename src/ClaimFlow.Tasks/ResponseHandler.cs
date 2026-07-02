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
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string StageKey = "Stage";
    private const string StatusKey = "Status";
    private const string QueueName = "orchestrator-responses";

    [Function(nameof(ResponseHandler))]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = "messaging")] ServiceBusReceivedMessage message,
        [DurableClient] DurableTaskClient durableClient,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<ResponseHandler>();

        message.ApplicationProperties.TryGetValue(OrchestratorIdKey, out var o);
        message.ApplicationProperties.TryGetValue(StageKey, out var s);
        message.ApplicationProperties.TryGetValue(StatusKey, out var st);
        var orchestratorId = o as string;
        var stage = s as string;
        var status = st as string ?? "Ok";
        if (orchestratorId is null || stage is null)
        {
            logger.LogWarning("S2-Tasks response handler: missing routing properties, ignoring");
            return;
        }

        logger.LogInformation("S2-Tasks response handler: {Stage} replied ({Status}), resuming orchestration", stage, status);

        await durableClient.RaiseEventAsync(orchestratorId, $"{stage}Done", status, ct);
    }
}

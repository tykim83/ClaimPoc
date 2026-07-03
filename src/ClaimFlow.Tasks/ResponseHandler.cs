using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

// Receives brick replies on the shared responses queue and resumes the right
// orchestration. CorrelationId routes the event (it IS the orchestration
// instanceId); Stage names it.
public class ResponseHandler
{
    private const string CorrelationIdKey = "CorrelationId";
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

        message.ApplicationProperties.TryGetValue(CorrelationIdKey, out var c);
        message.ApplicationProperties.TryGetValue(StageKey, out var s);
        message.ApplicationProperties.TryGetValue(StatusKey, out var st);
        var correlationId = c as string;
        var stage = s as string;
        var status = st as string ?? "Ok";
        if (correlationId is null || stage is null)
        {
            logger.LogWarning("S2-Tasks response handler: missing routing properties, ignoring");
            return;
        }

        logger.LogInformation("S2-Tasks response handler: {Stage} replied ({Status}), resuming orchestration", stage, status);

        await durableClient.RaiseEventAsync(correlationId, $"{stage}Done", status, ct);
    }
}

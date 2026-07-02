using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

// Service Bus-triggered entry point for the WSM/orchestrator. Receives the claim
// event Comms publishes to `orchestrator-in`, re-establishes the CorrelationId log
// scope from the message's application properties (nothing is passed by hand), and
// starts the durable orchestration using CorrelationId AS the instanceId.
//
// NOTE (this slice): Service Bus is not wired in AppHost yet and Comms does not
// publish, so this does not run end-to-end. The connection/queue names below are
// the intended topology; they become live in the next slice.
public class OrchestratorStarter
{
    private const string QueueName = "orchestrator-in";
    private const string ConnectionName = "messaging";
    private const string CorrelationIdKey = "CorrelationId";

    [Function(nameof(OrchestratorStarter))]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = ConnectionName)] ServiceBusReceivedMessage message,
        [DurableClient] DurableTaskClient durableClient,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<OrchestratorStarter>();

        message.ApplicationProperties.TryGetValue(CorrelationIdKey, out var value);
        var correlationId = value as string;
        if (correlationId is null)
        {
            logger.LogWarning("S2-Tasks starter: message has no CorrelationId, ignoring");
            return;
        }

        logger.LogInformation("S2-Tasks  starter: received claim event from Comms");

        // instanceId (the orchestratorId) is durable-generated, distinct from the
        // business correlationId which we pass as the orchestration input.
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            nameof(ClaimOrchestrator),
            correlationId,
            cancellation: ct);

        logger.LogInformation("S2-Tasks  starter: started orchestration {InstanceId}", instanceId);
    }
}

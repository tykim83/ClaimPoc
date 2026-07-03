using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

// Picks up the claim event from Comms and starts the durable orchestration.
// The CorrelationId log scope is opened by the middleware.
public class OrchestratorStarter(ClaimIntakeMetrics metrics)
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
        metrics.S2TasksReceived.Add(1);

        // instanceId (the orchestratorId) is durable-generated, distinct from the
        // business correlationId which we pass as the orchestration input.
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            nameof(ClaimOrchestrator),
            correlationId,
            cancellation: ct);

        logger.LogInformation("S2-Tasks  starter: started orchestration {InstanceId}", instanceId);
    }
}

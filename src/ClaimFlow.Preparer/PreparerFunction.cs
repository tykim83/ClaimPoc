using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Preparer;

public class PreparerFunction(IPreparerService service, ClaimIntakeMetrics metrics)
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string QueueName = "preparer-in";

    [Function("PreparerFunction")]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = "messaging")] ServiceBusReceivedMessage message,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<PreparerFunction>();

        var correlationId = (string)message.ApplicationProperties[CorrelationIdKey];

        // OrchestratorId is echoed straight back on the response so Tasks can route it.
        message.ApplicationProperties.TryGetValue(OrchestratorIdKey, out var oid);
        var orchestratorId = oid as string;

        metrics.S4PreparerReceived.Add(1);
        logger.LogInformation("S4-Preparer: received claim");

        await service.HandleAsync(correlationId, orchestratorId, ct);
    }
}

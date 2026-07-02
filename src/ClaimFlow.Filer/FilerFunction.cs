using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Filer;

public class FilerFunction(IFilerService service, ClaimIntakeMetrics metrics)
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string QueueName = "filer-in";

    [Function("FilerFunction")]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = "messaging")] ServiceBusReceivedMessage message,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<FilerFunction>();

        message.ApplicationProperties.TryGetValue(CorrelationIdKey, out var cid);
        var correlationId = cid as string;
        if (correlationId is null)
        {
            logger.LogWarning("S5-Filer: message has no CorrelationId, ignoring");
            return;
        }

        // OrchestratorId is echoed straight back on the response so Tasks can route it.
        message.ApplicationProperties.TryGetValue(OrchestratorIdKey, out var oid);
        var orchestratorId = oid as string;

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = correlationId,
        });

        metrics.S5FilerReceived.Add(1);
        logger.LogInformation("S5-Filer: received claim");

        await service.HandleAsync(correlationId, orchestratorId, ct);
    }
}

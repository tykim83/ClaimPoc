using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Preparer;

public interface IPreparerService
{
    Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default);
}

// Injected service, same shape as Comms. CorrelationId + OrchestratorId are passed in
// only as message *data*; the log lines pick up CorrelationId from the ambient scope.
public class PreparerService(ILogger<PreparerService> logger, ServiceBusClient serviceBusClient, ClaimIntakeMetrics metrics) : IPreparerService
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string StageKey = "Stage";
    private const string Stage = "Preparer";
    private const string ResponseQueue = "orchestrator-responses";

    public async Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default)
    {
        logger.LogInformation("S4-Preparer service: started process");

        // Fake work.
        await Task.Delay(Random.Shared.Next(200, 800), ct);
        metrics.S4PreparerProcessed.Add(1);

        // Publish the response to the shared responses queue. CorrelationId (business id)
        // + OrchestratorId (durable instanceId, echoed back for RaiseEvent routing) + Stage
        // ride as app properties.
        var sender = serviceBusClient.CreateSender(ResponseQueue);
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { claimId = correlationId }))
        {
            ApplicationProperties =
            {
                [CorrelationIdKey] = correlationId,
                [StageKey] = Stage,
            },
        };
        if (orchestratorId is not null)
        {
            message.ApplicationProperties[OrchestratorIdKey] = orchestratorId;
        }
        await sender.SendMessageAsync(message, ct);
        metrics.S4PreparerSent.Add(1);

        logger.LogInformation("S4-Preparer service: published response to {Queue}", ResponseQueue);
    }
}

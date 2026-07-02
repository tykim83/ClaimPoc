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
    private const string StatusKey = "Status";
    private const string Stage = "Preparer";
    private const string ResponseQueue = "orchestrator-responses";
    private const double SoftFailChance = 0.10;

    // One sender, reused for the singleton's lifetime. Creating a sender per message opens
    // a new AMQP link every time and leaks handles -> QuotaExceeded (max 199/connection).
    private readonly ServiceBusSender _sender = serviceBusClient.CreateSender(ResponseQueue);

    public async Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default)
    {
        logger.LogInformation("S4-Preparer service: started process");

        // Fake work.
        await Task.Delay(Random.Shared.Next(200, 800), ct);

        // "Poison" messages (~6%, decided by the claim id so the failure is stable across
        // retries): always throw -> retried -> eventually dead-lettered. This claim's
        // orchestration is left waiting.
        if (correlationId[^1] == '0')
        {
            metrics.S4PreparerFailed.Add(1);
            logger.LogError("S4-Preparer service: poison message, will dead-letter after retries");
            throw new InvalidOperationException("Simulated Preparer failure");
        }

        // ~10% soft failure: reported as Failed on the response so the orchestrator can
        // stop the claim; otherwise Ok.
        string status;
        if (Random.Shared.NextDouble() < SoftFailChance)
        {
            metrics.S4PreparerFailed.Add(1);
            logger.LogWarning("S4-Preparer service: simulated soft failure");
            status = "Failed";
        }
        else
        {
            metrics.S4PreparerProcessed.Add(1);
            status = "Ok";
        }

        // Publish the response to the shared responses queue. CorrelationId (business id)
        // + OrchestratorId (durable instanceId, echoed back for RaiseEvent routing) + Stage
        // ride as app properties.
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { claimId = correlationId }))
        {
            ApplicationProperties =
            {
                [CorrelationIdKey] = correlationId,
                [StageKey] = Stage,
                [StatusKey] = status,
            },
        };
        if (orchestratorId is not null)
        {
            message.ApplicationProperties[OrchestratorIdKey] = orchestratorId;
        }
        await _sender.SendMessageAsync(message, ct);
        metrics.S4PreparerSent.Add(1);

        logger.LogInformation("S4-Preparer service: published response to {Queue}", ResponseQueue);
    }
}

using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Filer;

public interface IFilerService
{
    Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default);
}

// Injected service, same shape as Comms. CorrelationId + OrchestratorId are passed in
// only as message *data*; the log lines pick up CorrelationId from the ambient scope.
public class FilerService(ILogger<FilerService> logger, ServiceBusClient serviceBusClient, ClaimIntakeMetrics metrics) : IFilerService
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string StageKey = "Stage";
    private const string StatusKey = "Status";
    private const string Stage = "Filer";
    private const string ResponseQueue = "orchestrator-responses";
    private const double SoftFailChance = 0.10;

    // One sender, reused for the singleton's lifetime. Creating a sender per message opens
    // a new AMQP link every time and leaks handles -> QuotaExceeded (max 199/connection).
    private readonly ServiceBusSender _sender = serviceBusClient.CreateSender(ResponseQueue);

    public async Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default)
    {
        logger.LogInformation("S5-Filer service: started process");

        // Fake work.
        await Task.Delay(Random.Shared.Next(200, 800), ct);

        // "Poison" messages (~6%, decided by the claim id so the failure is stable across
        // retries): always throw -> retried -> eventually dead-lettered. This claim's
        // orchestration is left waiting.
        if (correlationId[^1] == '0')
        {
            metrics.S5FilerFailed.Add(1);
            logger.LogError("S5-Filer service: poison message, will dead-letter after retries");
            throw new InvalidOperationException("Simulated Filer failure");
        }

        // ~10% soft failure: reported as Failed on the response so the orchestrator can
        // stop the claim; otherwise Ok.
        string status;
        if (Random.Shared.NextDouble() < SoftFailChance)
        {
            metrics.S5FilerFailed.Add(1);
            logger.LogWarning("S5-Filer service: simulated soft failure");
            status = "Failed";
        }
        else
        {
            metrics.S5FilerProcessed.Add(1);
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
        metrics.S5FilerSent.Add(1);

        logger.LogInformation("S5-Filer service: published response to {Queue}", ResponseQueue);
    }
}

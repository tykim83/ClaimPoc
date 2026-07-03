using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Filer;

public interface IFilerService
{
    Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default);
}

// CorrelationId/OrchestratorId come in as message data only; the log lines get their
// CorrelationId from the scope the middleware opened.
public class FilerService(ILogger<FilerService> logger, ServiceBusClient serviceBusClient, ClaimIntakeMetrics metrics) : IFilerService
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string StageKey = "Stage";
    private const string StatusKey = "Status";
    private const string Stage = "Filer";
    private const string ResponseQueue = "orchestrator-responses";
    private const double SoftFailChance = 0.10;
    private const double HardFailChance = 0.02;   // ~2% dead-letter at this brick; tune freely

    // one sender per queue, reused: a sender per message leaks AMQP links (cap 199)
    private readonly ServiceBusSender _sender = serviceBusClient.CreateSender(ResponseQueue);

    public async Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default)
    {
        logger.LogInformation("S5-Filer service: started process");

        // fake work
        await Task.Delay(Random.Shared.Next(200, 800), ct);

        // hard failure: throw -> retried -> dead-lettered. Must be deterministic per
        // claim+stage, otherwise the retry would succeed and nothing ever reaches the DLQ.
        if (FailureChaos.HardFails(correlationId, Stage, HardFailChance))
        {
            metrics.S5FilerFailed.Add(1);
            logger.LogError("S5-Filer service: poison message, will dead-letter after retries");
            throw new InvalidOperationException("Simulated Filer failure");
        }

        // soft failure: reply Failed so the orchestrator stops the claim cleanly
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

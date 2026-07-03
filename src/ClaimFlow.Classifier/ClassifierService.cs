using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Classifier;

public interface IClassifierService
{
    Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default);
}

// Injected service, same shape as Comms. CorrelationId + OrchestratorId are passed in
// only as message *data*; the log lines pick up CorrelationId from the ambient scope.
public class ClassifierService(ILogger<ClassifierService> logger, ServiceBusClient serviceBusClient, ClaimIntakeMetrics metrics) : IClassifierService
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string StageKey = "Stage";
    private const string StatusKey = "Status";
    private const string Stage = "Classifier";
    private const string ResponseQueue = "orchestrator-responses";
    private const double SoftFailChance = 0.10;
    private const double HardFailChance = 0.02;   // ~2% dead-letter at this brick; tune freely

    // One sender, reused for the singleton's lifetime. Creating a sender per message opens
    // a new AMQP link every time and leaks handles -> QuotaExceeded (max 199/connection).
    private readonly ServiceBusSender _sender = serviceBusClient.CreateSender(ResponseQueue);

    public async Task HandleAsync(string correlationId, string? orchestratorId, CancellationToken ct = default)
    {
        logger.LogInformation("S3-Classifier service: started process");

        // Fake work.
        await Task.Delay(Random.Shared.Next(200, 800), ct);

        // Small hard failure -> throw -> retried -> dead-lettered. Deterministic per
        // (claim, stage) so retries fail identically (message truly reaches the DLQ) and
        // each brick loses an independent slice. See FailureChaos.
        if (FailureChaos.HardFails(correlationId, Stage, HardFailChance))
        {
            metrics.S3ClassifierFailed.Add(1);
            logger.LogError("S3-Classifier service: poison message, will dead-letter after retries");
            throw new InvalidOperationException("Simulated Classifier failure");
        }

        // ~10% soft failure: reported as Failed on the response so the orchestrator can
        // stop the claim; otherwise Ok.
        string status;
        if (Random.Shared.NextDouble() < SoftFailChance)
        {
            metrics.S3ClassifierFailed.Add(1);
            logger.LogWarning("S3-Classifier service: simulated soft failure");
            status = "Failed";
        }
        else
        {
            metrics.S3ClassifierProcessed.Add(1);
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
        metrics.S3ClassifierSent.Add(1);

        logger.LogInformation("S3-Classifier service: published response to {Queue}", ResponseQueue);
    }
}

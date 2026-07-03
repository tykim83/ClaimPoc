using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Comms;

public interface ICommsService
{
    Task StartProcessAsync(string correlationId, CancellationToken ct = default);
}

// The correlationId parameter is only message payload. The log lines below still carry
// CorrelationId because the scope opened in the function flows here via AsyncLocal.
public class CommsService(ILogger<CommsService> logger, ServiceBusClient serviceBusClient, ClaimIntakeMetrics metrics) : ICommsService
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string QueueName = "orchestrator-in";

    // one sender per queue, reused: a sender per message leaks AMQP links (cap 199)
    private readonly ServiceBusSender _sender = serviceBusClient.CreateSender(QueueName);

    public async Task StartProcessAsync(string correlationId, CancellationToken ct = default)
    {
        logger.LogInformation("S1-Comms service: started process");

        // fake work
        await Task.Delay(Random.Shared.Next(200, 800), ct);
        metrics.S1CommsProcessed.Add(1);

        // traceparent is propagated automatically by the Service Bus SDK
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { claimId = correlationId }))
        {
            ApplicationProperties =
            {
                [CorrelationIdKey] = correlationId,
            },
        };
        await _sender.SendMessageAsync(message, ct);

        logger.LogInformation("S1-Comms service: published claim event to {Queue}", QueueName);
    }
}

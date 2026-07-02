using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Comms;

public interface ICommsService
{
    Task StartProcessAsync(string correlationId, CancellationToken ct = default);
}

// Injected service. Its ILogger comes purely from DI. The CorrelationId is passed
// in only as message *data* (it has to ride the Service Bus app property); the log
// lines below still pick up CorrelationId from the ambient scope the function opened
// (AsyncLocal), NOT from the parameter — that's the plumbing being proven.
public class CommsService(ILogger<CommsService> logger, ServiceBusClient serviceBusClient, ClaimIntakeMetrics metrics) : ICommsService
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string QueueName = "orchestrator-in";

    // One sender, reused for the singleton's lifetime. Creating a sender per message opens
    // a new AMQP link every time and leaks handles -> QuotaExceeded (max 199/connection).
    private readonly ServiceBusSender _sender = serviceBusClient.CreateSender(QueueName);

    public async Task StartProcessAsync(string correlationId, CancellationToken ct = default)
    {
        logger.LogInformation("S1-Comms service: started process");

        // Fake work.
        await Task.Delay(Random.Shared.Next(200, 800), ct);
        metrics.S1CommsProcessed.Add(1);

        // Publish the claim event to Tasks. CorrelationId rides as an application
        // property; the body is a tiny mock DTO. Trace context (traceparent) is added
        // automatically by Azure.Messaging.ServiceBus instrumentation.
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { claimId = correlationId }))
        {
            ApplicationProperties =
            {
                [CorrelationIdKey] = correlationId,
            },
        };
        await _sender.SendMessageAsync(message, ct);
        metrics.S1CommsSent.Add(1);

        logger.LogInformation("S1-Comms service: published claim event to {Queue}", QueueName);
    }
}

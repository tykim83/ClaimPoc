using Azure.Messaging.ServiceBus;
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
public class CommsService(ILogger<CommsService> logger, ServiceBusClient serviceBusClient) : ICommsService
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string QueueName = "orchestrator-in";

    public async Task StartProcessAsync(string correlationId, CancellationToken ct = default)
    {
        logger.LogInformation("S1-Comms service: started process");

        // Publish the claim event to Tasks. CorrelationId rides as an application
        // property; the body is a tiny mock DTO. Trace context (traceparent) is added
        // automatically by Azure.Messaging.ServiceBus instrumentation.
        var sender = serviceBusClient.CreateSender(QueueName);
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new { claimId = correlationId }))
        {
            ApplicationProperties =
            {
                [CorrelationIdKey] = correlationId,
            },
        };
        await sender.SendMessageAsync(message, ct);

        logger.LogInformation("S1-Comms service: published claim event to {Queue}", QueueName);
    }
}

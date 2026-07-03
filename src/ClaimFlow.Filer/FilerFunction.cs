using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Filer;

public class FilerFunction(IFilerService service, ClaimIntakeMetrics metrics)
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string QueueName = "filer-in";

    [Function("FilerFunction")]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = "messaging")] ServiceBusReceivedMessage message,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<FilerFunction>();

        var correlationId = (string)message.ApplicationProperties[CorrelationIdKey];

        // count the claim once, not once per redelivery
        if (message.DeliveryCount == 1)
        {
            metrics.S5FilerReceived.Add(1);
        }

        logger.LogInformation("S5-Filer: received claim");

        await service.HandleAsync(correlationId, ct);
    }

    // Fires when a message lands in this queue's DLQ: a claim we lost. Counting the
    // actual DLQ arrival (instead of guessing from the failing handler) gives exactly
    // one per lost claim, no matter how many retries it took to get here.
    [Function("FilerDeadLetter")]
    public void DeadLetter(
        [ServiceBusTrigger(QueueName + "/$deadletterqueue", Connection = "messaging")] ServiceBusReceivedMessage message,
        FunctionContext context)
    {
        metrics.S5FilerDeadLettered.Add(1);
        context.GetLogger<FilerFunction>().LogError("S5-Filer: claim dead-lettered");
    }
}

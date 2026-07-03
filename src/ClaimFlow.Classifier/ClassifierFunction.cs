using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Classifier;

public class ClassifierFunction(IClassifierService service, ClaimIntakeMetrics metrics)
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string QueueName = "classifier-in";

    [Function("ClassifierFunction")]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = "messaging")] ServiceBusReceivedMessage message,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<ClassifierFunction>();

        var correlationId = (string)message.ApplicationProperties[CorrelationIdKey];

        // count the claim once, not once per redelivery
        if (message.DeliveryCount == 1)
        {
            metrics.S3ClassifierReceived.Add(1);
        }

        logger.LogInformation("S3-Classifier: received claim");

        await service.HandleAsync(correlationId, ct);
    }

    // Fires when a message lands in this queue's DLQ: a claim we lost. Counting the
    // actual DLQ arrival (instead of guessing from the failing handler) gives exactly
    // one per lost claim, no matter how many retries it took to get here.
    [Function("ClassifierDeadLetter")]
    public void DeadLetter(
        [ServiceBusTrigger(QueueName + "/$deadletterqueue", Connection = "messaging")] ServiceBusReceivedMessage message,
        FunctionContext context)
    {
        metrics.S3ClassifierDeadLettered.Add(1);
        context.GetLogger<ClassifierFunction>().LogError("S3-Classifier: claim dead-lettered");
    }
}

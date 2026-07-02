using Azure.Messaging.ServiceBus;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Classifier;

public class ClassifierFunction(IClassifierService service, ClaimIntakeMetrics metrics)
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string OrchestratorIdKey = "OrchestratorId";
    private const string QueueName = "classifier-in";

    [Function("ClassifierFunction")]
    public async Task Run(
        [ServiceBusTrigger(QueueName, Connection = "messaging")] ServiceBusReceivedMessage message,
        FunctionContext context,
        CancellationToken ct)
    {
        var logger = context.GetLogger<ClassifierFunction>();

        var correlationId = (string)message.ApplicationProperties[CorrelationIdKey];

        // OrchestratorId is echoed straight back on the response so Tasks can route it.
        message.ApplicationProperties.TryGetValue(OrchestratorIdKey, out var oid);
        var orchestratorId = oid as string;

        metrics.S3ClassifierReceived.Add(1);
        logger.LogInformation("S3-Classifier: received claim");

        await service.HandleAsync(correlationId, orchestratorId, ct);
    }
}

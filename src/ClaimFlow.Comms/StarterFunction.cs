using System.Net;
using ClaimFlow.ServiceDefaults;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Comms;

public class StarterFunction(ICommsService commsService, ClaimIntakeMetrics metrics)
{
    private const string CorrelationIdKey = "CorrelationId";

    [Function("Starter")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<StarterFunction>();
        var correlationId = Guid.NewGuid().ToString("N");

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = correlationId,
        });

        metrics.EmailReceived.Add(1);

        logger.LogInformation("Comms function: received email, CorrelationId generated");
        commsService.StartProcess();

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString(correlationId);
        return response;
    }
}

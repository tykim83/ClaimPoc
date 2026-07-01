using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Intake;

public class StarterFunction(IIntakeService intakeService)
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

        logger.LogInformation("Intake function: created claim, CorrelationId generated");
        intakeService.StartProcess();

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString(correlationId);
        return response;
    }
}

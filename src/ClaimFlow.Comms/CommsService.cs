using Microsoft.Extensions.Logging;

namespace ClaimFlow.Comms;

public interface ICommsService
{
    void StartProcess();
}

// Injected service. It gets its ILogger purely via DI and is NEVER handed the
// CorrelationId. If the id shows up on its log line, it flowed in via the log
// scope the function opened (AsyncLocal in the logging scope provider) — even
// though the function's logger came from FunctionContext and this one from DI.
public class CommsService(ILogger<CommsService> logger) : ICommsService
{
    public void StartProcess()
    {
        logger.LogInformation("Comms service: started process (no CorrelationId passed to me)");
    }
}

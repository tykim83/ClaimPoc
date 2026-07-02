using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Tasks;

// The WSM orchestrator. Stub for this slice: it just logs that it started. The
// fan-out to the three bricks (via activities + WaitForExternalEvent) arrives in
// later slices. instanceId == CorrelationId, so context.InstanceId is the claim's
// CorrelationId — used here for the log scope.
//
// Determinism: no direct I/O here; log via CreateReplaySafeLogger so replays don't
// duplicate log lines (the durable analog of the isolated-worker duplicate-log fix).
public class ClaimOrchestrator
{
    private const string CorrelationIdKey = "CorrelationId";

    [Function(nameof(ClaimOrchestrator))]
    public void Run([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger<ClaimOrchestrator>();

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = context.InstanceId,
        });

        logger.LogInformation("S2-Tasks Orchestrator: started for claim (stub — no bricks yet)");
    }
}

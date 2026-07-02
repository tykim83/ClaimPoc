using Microsoft.Extensions.Logging;

namespace ClaimFlow.Dashboard.Services;

// Feature 1: kick off N claim runs by hitting the Comms HTTP trigger (s1-comms).
// Comms owns CorrelationId creation + the first audit/metric, then publishes to
// orchestrator-in — so a UI-started run is identical to a "real" email intake.
// The HttpClient base address is the Aspire service-discovery name (see Program.cs).
public class ClaimStarter(HttpClient httpClient, ILogger<ClaimStarter> logger)
{
    private const string StarterRoute = "/api/Starter";
    private const int MaxConcurrency = 20;

    public async Task<int> StartAsync(int count, CancellationToken ct = default)
    {
        var sent = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = ct },
            async (_, token) =>
            {
                using var response = await httpClient.PostAsync(StarterRoute, content: null, token);
                response.EnsureSuccessStatusCode();
                Interlocked.Increment(ref sent);
            });

        logger.LogInformation("S6-Dashboard: started {Count} claim run(s) via Comms", sent);
        return sent;
    }
}

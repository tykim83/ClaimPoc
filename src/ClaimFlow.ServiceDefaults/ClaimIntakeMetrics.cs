using System.Diagnostics.Metrics;

namespace ClaimFlow.ServiceDefaults;

public sealed class ClaimIntakeMetrics
{
    public Counter<long> S1CommsReceived { get; }
    public Counter<long> S1CommsSent { get; }
    public Counter<long> S1CommsProcessed { get; }

    public ClaimIntakeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.MeterName);

        S1CommsReceived = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsReceived, description: "Claims received by Comms.");
        S1CommsSent = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsSent, description: "Claim events sent by Comms.");
        S1CommsProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsProcessed, description: "Claims processed by Comms.");
    }
}

public static class Telemetry
{
    public const string MeterName = "ClaimIntakeMetrics";

    // All metric names live here together. Each service gets 3 (received/sent/processed),
    // prefixed by its stage. For now: S1 Comms. Add S2 Tasks / bricks below the same way.
    public static class Metrics
    {
        public const string S1CommsReceived = "claimflow.comms.received";
        public const string S1CommsSent = "claimflow.comms.sent";
        public const string S1CommsProcessed = "claimflow.comms.processed";
    }
}

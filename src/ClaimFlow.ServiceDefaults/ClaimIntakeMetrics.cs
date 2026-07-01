using System.Diagnostics.Metrics;

namespace ClaimFlow.ServiceDefaults;

public sealed class ClaimIntakeMetrics
{
    public Counter<long> EmailReceived { get; }

    public ClaimIntakeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.MeterName);

        EmailReceived = meter.CreateCounter<long>(
            Telemetry.Metrics.EmailReceived,
            unit: "{email}",
            description: "Emails received by Comms.");
    }
}

public static class Telemetry
{
    public const string MeterName = "ClaimIntakeMetrics";

    public static class Metrics
    {
        public const string EmailReceived = "claimflow.comms.email.received";
    }
}

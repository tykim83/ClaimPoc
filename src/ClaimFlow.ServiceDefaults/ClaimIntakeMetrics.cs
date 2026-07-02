using System.Diagnostics.Metrics;

namespace ClaimFlow.ServiceDefaults;

public sealed class ClaimIntakeMetrics
{
    public Counter<long> S1CommsReceived { get; }
    public Counter<long> S1CommsSent { get; }
    public Counter<long> S1CommsProcessed { get; }

    public Counter<long> S3ClassifierReceived { get; }
    public Counter<long> S3ClassifierSent { get; }
    public Counter<long> S3ClassifierProcessed { get; }
    public Counter<long> S3ClassifierFailed { get; }

    public Counter<long> S4PreparerReceived { get; }
    public Counter<long> S4PreparerSent { get; }
    public Counter<long> S4PreparerProcessed { get; }
    public Counter<long> S4PreparerFailed { get; }

    public Counter<long> S5FilerReceived { get; }
    public Counter<long> S5FilerSent { get; }
    public Counter<long> S5FilerProcessed { get; }
    public Counter<long> S5FilerFailed { get; }

    public ClaimIntakeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.MeterName);

        S1CommsReceived = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsReceived, description: "Claims received by Comms.");
        S1CommsSent = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsSent, description: "Claim events sent by Comms.");
        S1CommsProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsProcessed, description: "Claims processed by Comms.");

        S3ClassifierReceived = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierReceived, description: "Claims received by Classifier.");
        S3ClassifierSent = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierSent, description: "Responses sent by Classifier.");
        S3ClassifierProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierProcessed, description: "Claims processed by Classifier.");
        S3ClassifierFailed = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierFailed, description: "Claims failed by Classifier.");

        S4PreparerReceived = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerReceived, description: "Claims received by Preparer.");
        S4PreparerSent = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerSent, description: "Responses sent by Preparer.");
        S4PreparerProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerProcessed, description: "Claims processed by Preparer.");
        S4PreparerFailed = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerFailed, description: "Claims failed by Preparer.");

        S5FilerReceived = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerReceived, description: "Claims received by Filer.");
        S5FilerSent = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerSent, description: "Responses sent by Filer.");
        S5FilerProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerProcessed, description: "Claims processed by Filer.");
        S5FilerFailed = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerFailed, description: "Claims failed by Filer.");
    }
}

public static class Telemetry
{
    public const string MeterName = "ClaimIntakeMetrics";

    // All metric names live here together. Each service gets received / sent / processed,
    // prefixed by its stage; the bricks also get failed.
    public static class Metrics
    {
        public const string S1CommsReceived = "claimflow.comms.received";
        public const string S1CommsSent = "claimflow.comms.sent";
        public const string S1CommsProcessed = "claimflow.comms.processed";

        public const string S3ClassifierReceived = "claimflow.classifier.received";
        public const string S3ClassifierSent = "claimflow.classifier.sent";
        public const string S3ClassifierProcessed = "claimflow.classifier.processed";
        public const string S3ClassifierFailed = "claimflow.classifier.failed";

        public const string S4PreparerReceived = "claimflow.preparer.received";
        public const string S4PreparerSent = "claimflow.preparer.sent";
        public const string S4PreparerProcessed = "claimflow.preparer.processed";
        public const string S4PreparerFailed = "claimflow.preparer.failed";

        public const string S5FilerReceived = "claimflow.filer.received";
        public const string S5FilerSent = "claimflow.filer.sent";
        public const string S5FilerProcessed = "claimflow.filer.processed";
        public const string S5FilerFailed = "claimflow.filer.failed";
    }
}

using System.Diagnostics.Metrics;

namespace ClaimFlow.ServiceDefaults;

public sealed class ClaimIntakeMetrics
{
    public Counter<long> S1CommsReceived { get; }
    public Counter<long> S1CommsProcessed { get; }

    public Counter<long> S2TasksReceived { get; }
    public Counter<long> S2TasksProcessed { get; }
    public Counter<long> S2TasksFailed { get; }

    public Counter<long> S3ClassifierReceived { get; }
    public Counter<long> S3ClassifierProcessed { get; }
    public Counter<long> S3ClassifierFailed { get; }
    public Counter<long> S3ClassifierDeadLettered { get; }

    public Counter<long> S4PreparerReceived { get; }
    public Counter<long> S4PreparerProcessed { get; }
    public Counter<long> S4PreparerFailed { get; }
    public Counter<long> S4PreparerDeadLettered { get; }

    public Counter<long> S5FilerReceived { get; }
    public Counter<long> S5FilerProcessed { get; }
    public Counter<long> S5FilerFailed { get; }
    public Counter<long> S5FilerDeadLettered { get; }

    public ClaimIntakeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.MeterName);

        S1CommsReceived = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsReceived, description: "Claims received by Comms.");
        S1CommsProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S1CommsProcessed, description: "Claims processed by Comms.");

        S2TasksReceived = meter.CreateCounter<long>(Telemetry.Metrics.S2TasksReceived, description: "Claims received by Tasks (orchestration started).");
        S2TasksProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S2TasksProcessed, description: "Claims completed successfully by Tasks.");
        S2TasksFailed = meter.CreateCounter<long>(Telemetry.Metrics.S2TasksFailed, description: "Claims failed by Tasks.");

        S3ClassifierReceived = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierReceived, description: "Claims entering Classifier (counted once, not per retry).");
        S3ClassifierProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierProcessed, description: "Claims Classifier handled ok.");
        S3ClassifierFailed = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierFailed, description: "Claims Classifier rejected (reported back to Tasks).");
        S3ClassifierDeadLettered = meter.CreateCounter<long>(Telemetry.Metrics.S3ClassifierDeadLettered, description: "Claims lost to Classifier's dead-letter queue.");

        S4PreparerReceived = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerReceived, description: "Claims entering Preparer (counted once, not per retry).");
        S4PreparerProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerProcessed, description: "Claims Preparer handled ok.");
        S4PreparerFailed = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerFailed, description: "Claims Preparer rejected (reported back to Tasks).");
        S4PreparerDeadLettered = meter.CreateCounter<long>(Telemetry.Metrics.S4PreparerDeadLettered, description: "Claims lost to Preparer's dead-letter queue.");

        S5FilerReceived = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerReceived, description: "Claims entering Filer (counted once, not per retry).");
        S5FilerProcessed = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerProcessed, description: "Claims Filer handled ok.");
        S5FilerFailed = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerFailed, description: "Claims Filer rejected (reported back to Tasks).");
        S5FilerDeadLettered = meter.CreateCounter<long>(Telemetry.Metrics.S5FilerDeadLettered, description: "Claims lost to Filer's dead-letter queue.");
    }
}

public static class Telemetry
{
    public const string MeterName = "ClaimIntakeMetrics";

    // All metric names live here together, prefixed by stage. For a brick,
    // received = processed + failed + deadlettered once a run settles.
    public static class Metrics
    {
        public const string S1CommsReceived = "claimflow.comms.received";
        public const string S1CommsProcessed = "claimflow.comms.processed";

        public const string S2TasksReceived = "claimflow.tasks.received";
        public const string S2TasksProcessed = "claimflow.tasks.processed";
        public const string S2TasksFailed = "claimflow.tasks.failed";

        public const string S3ClassifierReceived = "claimflow.classifier.received";
        public const string S3ClassifierProcessed = "claimflow.classifier.processed";
        public const string S3ClassifierFailed = "claimflow.classifier.failed";
        public const string S3ClassifierDeadLettered = "claimflow.classifier.deadlettered";

        public const string S4PreparerReceived = "claimflow.preparer.received";
        public const string S4PreparerProcessed = "claimflow.preparer.processed";
        public const string S4PreparerFailed = "claimflow.preparer.failed";
        public const string S4PreparerDeadLettered = "claimflow.preparer.deadlettered";

        public const string S5FilerReceived = "claimflow.filer.received";
        public const string S5FilerProcessed = "claimflow.filer.processed";
        public const string S5FilerFailed = "claimflow.filer.failed";
        public const string S5FilerDeadLettered = "claimflow.filer.deadlettered";
    }
}

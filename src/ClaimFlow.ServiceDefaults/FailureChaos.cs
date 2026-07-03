namespace ClaimFlow.ServiceDefaults;

// Deterministic fake failures for the bricks. The verdict is fixed per claim+stage:
// retries fail the same way (so a hard failure really reaches the DLQ instead of
// succeeding on redelivery), and each brick fails its own independent slice.
public static class FailureChaos
{
    // FNV-1a hash of id+stage mapped to [0,1). Not process-seeded, so the answer
    // survives worker restarts.
    public static bool HardFails(string claimId, string stage, double probability)
    {
        uint h = 2166136261u;
        foreach (var c in claimId) { h ^= c; h *= 16777619u; }
        foreach (var c in stage) { h ^= c; h *= 16777619u; }
        return h / (double)uint.MaxValue < probability;
    }
}

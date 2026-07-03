namespace ClaimFlow.ServiceDefaults;

// Deterministic fake failures for the mock bricks. The verdict is stable per (claimId,
// stage), which matters two ways:
//   * stable across retries  -> a "hard" failure fails every redelivery, so the message
//     actually exhausts its retries and reaches the dead-letter queue (a per-delivery
//     random roll would likely succeed on retry and self-heal, never hitting the DLQ);
//   * independent per stage   -> each brick loses its own slice, so a claim can clear the
//     classifier and still dead-letter at the filer.
public static class FailureChaos
{
    // True for ~`probability` of (claimId, stage) pairs, deterministically. FNV-1a hash of
    // id+stage mapped to [0,1) — not process-seeded, so a redelivery (even after a worker
    // restart) gets the same answer.
    public static bool HardFails(string claimId, string stage, double probability)
    {
        uint h = 2166136261u;
        foreach (var c in claimId) { h ^= c; h *= 16777619u; }
        foreach (var c in stage) { h ^= c; h *= 16777619u; }
        return h / (double)uint.MaxValue < probability;
    }
}

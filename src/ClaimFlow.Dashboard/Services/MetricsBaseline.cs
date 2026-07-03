using System.Collections.Concurrent;

namespace ClaimFlow.Dashboard.Services;

// Prometheus counters are cumulative and live inside each service's OTel SDK — the
// dashboard can't reset them. "Clear metrics" instead records the current totals here as
// a per-counter zero-offset; the panel then shows (current - baseline). A singleton so the
// offset survives across the transient MetricsClient / re-renders.
public class MetricsBaseline
{
    private readonly ConcurrentDictionary<(string Stage, string Measure), long> offsets = new();

    // Snapshot the given raw totals as the new zero point.
    public void Capture(IReadOnlyList<MetricPoint> raw)
    {
        offsets.Clear();
        foreach (var p in raw)
        {
            offsets[(p.Stage, p.Measure)] = p.Value;
        }
    }

    // Subtract the baseline from raw totals. If a counter is now below its baseline the
    // service restarted (counters reset to 0), so drop that stale offset and show the raw value.
    public IReadOnlyList<MetricPoint> Adjust(IReadOnlyList<MetricPoint> raw)
    {
        var result = new List<MetricPoint>(raw.Count);
        foreach (var p in raw)
        {
            var offset = offsets.GetValueOrDefault((p.Stage, p.Measure));
            if (p.Value < offset)
            {
                offsets.TryRemove((p.Stage, p.Measure), out _);
                offset = 0;
            }
            result.Add(p with { Value = p.Value - offset });
        }
        return result;
    }
}

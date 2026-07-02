using System.Globalization;

namespace ClaimFlow.Dashboard.Services;

public record FunnelStage(string Stage, long Count);

public record MetricsSnapshot(
    bool Available,
    string? Error,
    IReadOnlyList<FunnelStage> Funnel,
    long Completed,
    long Failed);

// Scrapes the OTel Collector's Prometheus endpoint and turns the raw counters into
// the pipeline funnel. Counter names come through as e.g. claimflow_comms_received_total
// (dots -> underscores, monotonic counters get a _total suffix), so we match by prefix
// to stay robust to the suffix and sum across any duplicate series.
public class MetricsClient(HttpClient httpClient)
{
    public bool Configured => httpClient.BaseAddress is not null;

    public async Task<MetricsSnapshot> GetAsync(CancellationToken ct = default)
    {
        if (!Configured)
        {
            return Empty("Collector endpoint not configured.");
        }

        try
        {
            var text = await httpClient.GetStringAsync("/metrics", ct);
            var totals = Parse(text);

            var funnel = new List<FunnelStage>
            {
                new("Comms", Sum(totals, "claimflow_comms_received")),
                new("Classifier", Sum(totals, "claimflow_classifier_received")),
                new("Preparer", Sum(totals, "claimflow_preparer_received")),
                new("Filer", Sum(totals, "claimflow_filer_received")),
                new("Completed", Sum(totals, "claimflow_filer_processed")),
            };

            var failed = Sum(totals, "claimflow_classifier_failed")
                + Sum(totals, "claimflow_preparer_failed")
                + Sum(totals, "claimflow_filer_failed");

            return new MetricsSnapshot(true, null, funnel, funnel[^1].Count, failed);
        }
        catch (Exception ex)
        {
            return Empty(ex.Message);
        }
    }

    private static MetricsSnapshot Empty(string error) =>
        new(false, error, Array.Empty<FunnelStage>(), 0, 0);

    private static long Sum(IReadOnlyDictionary<string, double> totals, string prefix)
    {
        double sum = 0;
        foreach (var (name, value) in totals)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                sum += value;
            }
        }
        return (long)sum;
    }

    // Minimal Prometheus text-format parser: sum value per metric name (labels ignored).
    private static Dictionary<string, double> Parse(string text)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var brace = line.IndexOf('{');
            string name;
            string valuePart;
            if (brace >= 0)
            {
                name = line[..brace];
                var close = line.IndexOf('}');
                valuePart = close >= 0 ? line[(close + 1)..].Trim() : "";
            }
            else
            {
                var sp = line.IndexOf(' ');
                if (sp < 0) continue;
                name = line[..sp];
                valuePart = line[(sp + 1)..].Trim();
            }

            var token = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (token.Length == 0) continue;
            if (double.TryParse(token[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                result[name] = result.GetValueOrDefault(name) + value;
            }
        }
        return result;
    }
}

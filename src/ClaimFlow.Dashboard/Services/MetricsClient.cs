using System.Globalization;

namespace ClaimFlow.Dashboard.Services;

public record MetricPoint(string Stage, string Measure, long Value);

public record MetricsSnapshot(
    bool Available,
    string? Error,
    IReadOnlyList<string> Stages,
    IReadOnlyList<string> Measures,
    IReadOnlyList<MetricPoint> Points);

// Scrapes the OTel Collector's Prometheus endpoint and returns ONLY the counters we
// define (the "claimflow.*" meter) — never the framework/runtime/http metrics. Nothing
// is curated or renamed: every claimflow counter that's present shows up, discovered
// from its own name (claimflow_<stage>_<measure>[_total]).
public class MetricsClient(HttpClient httpClient, MetricsBaseline baseline)
{
    private const string Prefix = "claimflow_";
    private static readonly string[] StageOrder = ["comms", "tasks", "classifier", "preparer", "filer"];
    private static readonly string[] MeasureOrder = ["received", "processed", "failed", "deadlettered"];

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
            var points = baseline.Adjust(Parse(text));
            var stages = points.Select(p => p.Stage).Distinct().OrderBy(RankBy(StageOrder)).ToList();
            var measures = points.Select(p => p.Measure).Distinct().OrderBy(RankBy(MeasureOrder)).ToList();
            return new MetricsSnapshot(true, null, stages, measures, points);
        }
        catch (Exception ex)
        {
            return Empty(ex.Message);
        }
    }

    // "Clear": record the current raw totals as the new zero-offset. The counters keep
    // climbing in each service; the panel just shows the delta since this point.
    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (!Configured)
        {
            return;
        }

        var text = await httpClient.GetStringAsync("/metrics", ct);
        baseline.Capture(Parse(text));
    }

    private static Func<string, int> RankBy(string[] order) => s =>
    {
        var i = Array.IndexOf(order, s);
        return i < 0 ? int.MaxValue : i;
    };

    private static MetricsSnapshot Empty(string error) => new(false, error, [], [], []);

    private static List<MetricPoint> Parse(string text)
    {
        var totals = new Dictionary<(string Stage, string Measure), double>();

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

            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var body = name[Prefix.Length..];
            if (body.EndsWith("_total", StringComparison.Ordinal))
            {
                body = body[..^"_total".Length];
            }

            var us = body.IndexOf('_');
            if (us < 0)
            {
                continue;
            }

            var stage = body[..us];
            var measure = body[(us + 1)..];

            var token = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (token.Length == 0) continue;
            if (double.TryParse(token[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                var key = (stage, measure);
                totals[key] = totals.GetValueOrDefault(key) + value;
            }
        }

        return totals.Select(kv => new MetricPoint(kv.Key.Stage, kv.Key.Measure, (long)kv.Value)).ToList();
    }
}

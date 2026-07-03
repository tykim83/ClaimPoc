using ApexCharts;
using ClaimFlow.Dashboard.Components;
using ClaimFlow.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Read the append-only event log Tasks writes to Cosmos. The dashboard never writes.
builder.AddAzureCosmosClient("cosmos");

// Start runs by calling the Comms HTTP trigger. The host part is NOT a real
// address — "s1-comms" is the AppHost resource name. AddServiceDefaults registers
// the service-discovery HttpClient handler, which rewrites this to Comms' actual
// host:port at request time (from the env vars .WithReference(comms) injects).
builder.Services.AddHttpClient<ClaimStarter>(client =>
{
    client.BaseAddress = new Uri("http://s1-comms");
});

builder.Services.AddScoped<EventStore>();

// Holds the "cleared" zero-offset for the cumulative counters (singleton so it survives
// the transient MetricsClient). See MetricsBaseline.
builder.Services.AddSingleton<MetricsBaseline>();

// Scrapes the OTel Collector's Prometheus endpoint (injected by AppHost) for the
// metrics funnel. Base address is unset if the collector isn't wired — the client
// then reports "not configured" rather than throwing.
var collectorMetrics = builder.Configuration["COLLECTOR_METRICS_ENDPOINT"];
builder.Services.AddHttpClient<MetricsClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(collectorMetrics))
    {
        client.BaseAddress = new Uri(collectorMetrics);
    }
});

builder.Services.AddApexCharts();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

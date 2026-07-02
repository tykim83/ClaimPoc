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

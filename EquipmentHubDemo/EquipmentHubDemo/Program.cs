using EquipmentHubDemo.Components;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Infrastructure;
using EquipmentHubDemo.Live;
using EquipmentHubDemo.Workers;
using EquipmentHubDemo.Components.Pages;

var builder = WebApplication.CreateBuilder(args);

// Razor Components (Server + WASM islands)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Antiforgery (required by Razor Components endpoints)
builder.Services.AddAntiforgery();

// Optional: server-side HttpClient (WASM gets its own automatically)
builder.Services.AddHttpClient();

// Infra + services
builder.Services.AddSingleton<IMeasurementRepository>(sp =>
    new LiteDbMeasurementRepository(
        Path.Combine(builder.Environment.ContentRootPath, "data", "measurements.db")));

builder.Services.Configure<LiveCacheOptions>(builder.Configuration.GetSection(LiveCacheOptions.SectionName));
builder.Services.Configure<TtlWorkerOptions>(builder.Configuration.GetSection(TtlWorkerOptions.SectionName));
builder.Services.AddSingleton<ILiveCache, LiveCache>();

// Background services (broker + workers)
builder.Services.AddHostedService<ZmqBrokerService>();
builder.Services.AddHostedService<FilterStoreWorker>();
builder.Services.AddHostedService<TtlWorker>();
builder.Services.AddHostedService<LiveSubscriberWorker>();

var app = builder.Build();

// ---- Static files & framework assets (must precede WASM render mode) ----
app.UseStaticFiles();
app.MapStaticAssets();

// Antiforgery middleware must be in the pipeline
app.UseAntiforgery();

// ---------- Minimal read APIs for WASM UI ----------
app.MapGet("/api/keys", (ILiveCache cache) =>
{
    // returns: ["UXG-01:Power", "UXG-02:SNR", ...]
    return Results.Json(cache.Keys);
});

app.MapGet("/api/live", (string key, long? sinceTicks, ILiveCache cache) =>
{
    // returns: [{ x: "2025-09-25T19:20:31Z", y: 11.2 }, ...]
    var pts = cache.GetSeries(key);
    IEnumerable<object> result;

    if (sinceTicks is long t && t > 0)
        result = pts.Where(p => p.X.Ticks > t).Select(p => new { x = p.X, y = p.Y });
    else
        result = pts.TakeLast(500).Select(p => new { x = p.X, y = p.Y }); // initial snapshot

    return Results.Json(result);
});

// Optional: silence favicon 404s (if you only ship a PNG/SVG)
app.MapGet("/favicon.ico", () => Results.Redirect("/favicon.png"));

// ---------- Razor Components endpoints ----------
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddInteractiveWebAssemblyRenderMode()
   .AddAdditionalAssemblies(typeof(Home).Assembly);

// Ensure requests to the site root are served the Blazor bootstrapper
// so the router can activate the Home page instead of returning 404.
// app.MapFallbackToFile("apphost.html");

app.Run();

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EquipmentHubDemo.Components;
using EquipmentHubDemo.Client.Services;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Control;
using EquipmentHubDemo.Domain.Live;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Domain.Predict;
using Microsoft.Extensions.Configuration;
using EquipmentHubDemo.Infrastructure;
using EquipmentHubDemo.Infrastructure.Control;
using EquipmentHubDemo.Instrumentation;
using EquipmentHubDemo.Infrastructure.Predict;
using EquipmentHubDemo.Live;
using EquipmentHubDemo.Domain.Status;
using EquipmentHubDemo.Workers;
using EquipmentHubDemo.Components.Pages;
using Microsoft.Net.Http.Headers;
using EquipmentHubDemo.Status;

static bool IsHttpScheme(string? scheme) =>
    string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(args);

// Razor Components (Server + WASM islands)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Antiforgery (required by Razor Components endpoints)
builder.Services.AddAntiforgery();

// Optional: server-side HttpClient (WASM gets its own automatically)
builder.Services.AddHttpClient();
builder.Services.Configure<ApiClientOptions>(options =>
    builder.Configuration.GetSection(ApiClientOptions.SectionName).Bind(options));
builder.Services.AddScoped<IApiBaseUriProvider, ApiBaseUriProvider>();
builder.Services.AddScoped<ILiveMeasurementClient, HttpLiveMeasurementClient>();
builder.Services.AddScoped<ISystemStatusClient, HttpSystemStatusClient>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Random>(_ => Random.Shared);

builder.Services.Configure<SimulatedScpiOptions>(builder.Configuration.GetSection(SimulatedScpiOptions.SectionName));
builder.Services.Configure<PredictiveDiagnosticsOptions>(builder.Configuration.GetSection(PredictiveDiagnosticsOptions.SectionName));
builder.Services.Configure<KubernetesTrafficOptions>(builder.Configuration.GetSection(KubernetesTrafficOptions.SectionName));

var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?.Select(origin => origin?.Trim())
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin!)
    .ToArray() ?? Array.Empty<string>();

var allowedExactOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var allowedAuthorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var allowedHostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var entry in configuredOrigins)
{
    if (Uri.TryCreate(entry, UriKind.Absolute, out var uri) &&
        IsHttpScheme(uri.Scheme) &&
        !string.IsNullOrEmpty(uri.Host))
    {
        allowedExactOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
    }
    else if (entry.Contains(':'))
    {
        allowedAuthorities.Add(entry);
    }
    else
    {
        allowedHostnames.Add(entry);
    }
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevClient", policy =>
    {
        policy.WithMethods("GET")
            .WithHeaders(
                HeaderNames.Accept,
                HeaderNames.ContentType)
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                {
                    return false;
                }

                if (allowedExactOrigins.Contains(origin))
                {
                    return true;
                }

                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                    !IsHttpScheme(uri.Scheme))
                {
                    return false;
                }

                if (uri.IsLoopback)
                {
                    return true;
                }

                if (allowedAuthorities.Contains(uri.Authority))
                {
                    return true;
                }

                return allowedHostnames.Contains(uri.Host);
            });
    });
});

// Infra + services
builder.Services.AddSingleton<IMeasurementRepository>(sp =>
    new LiteDbMeasurementRepository(
        Path.Combine(builder.Environment.ContentRootPath, "data", "measurements.db")));
builder.Services.AddSingleton<IDiagnosticRepository>(sp =>
    new LiteDbDiagnosticRepository(
        Path.Combine(builder.Environment.ContentRootPath, "data", "diagnostics.db")));

builder.Services.Configure<LiveCacheOptions>(builder.Configuration.GetSection(LiveCacheOptions.SectionName));
builder.Services.Configure<TtlWorkerOptions>(builder.Configuration.GetSection(TtlWorkerOptions.SectionName));
builder.Services.Configure<FilterStoreOptions>(builder.Configuration.GetSection(FilterStoreOptions.SectionName));
builder.Services.AddSingleton<ILiveCache, LiveCache>();
builder.Services.AddSingleton<IScpiCommandClient, SimulatedScpiCommandClient>();
builder.Services.AddSingleton<IPredictiveDiagnosticsService, PredictiveDiagnosticsService>();
builder.Services.AddSingleton<IPredictiveMaintenanceService, PredictiveMaintenanceService>();
builder.Services.AddSingleton<IInstrumentConfigurationService, ScenarioConfigurationService>();
builder.Services.AddSingleton<IInstrumentCalibrationService, InstrumentCalibrationService>();
builder.Services.AddSingleton<IRfPathService, RfPathService>();
builder.Services.AddSingleton<INetworkTrafficOptimizer, KubernetesNetworkTrafficOptimizer>();
builder.Services.AddScoped<PredictiveStatusProvider>();
builder.Services.AddScoped<MonitoringStatusProvider>();
builder.Services.AddSingleton<ILiveCatalogProvider, LiveCatalogProvider>();
builder.Services.AddSingleton<IMeasurementPipeline, FilterStoreMeasurementPipeline>();
builder.Services.AddSingleton<ITtlCleanupService, TtlCleanupService>();

// Background services (broker + workers)
builder.Services.AddHostedService<ZmqBrokerService>();
builder.Services.AddHostedService<FilterStoreWorker>();
builder.Services.AddHostedService<TtlWorker>();
builder.Services.AddHostedService<LiveSubscriberWorker>();

var app = builder.Build();

// ---- Static files & framework assets (must precede WASM render mode) ----
app.UseStaticFiles();
app.MapStaticAssets();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.Request.Path.Equals("/_framework/blazor.boot.json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
            context.Response.Headers[HeaderNames.Pragma] = "no-cache";
            context.Response.Headers[HeaderNames.Expires] = "0";
        }

        return Task.CompletedTask;
    });

    await next();
});

// Antiforgery middleware must be in the pipeline
app.UseAntiforgery();

app.UseCors("DevClient");

// ---------- Minimal read APIs for WASM UI ----------
app.MapGet("/api/keys", (ILiveCatalogProvider provider) =>
{
    // returns: ["UXG-01:Temperature", "UXG-01:Humidity", "UXG-01:Power (240VAC)", ...]
    var catalog = provider.BuildCatalog();
    return Results.Json(catalog.GetAllKeys());
});

app.MapGet("/api/live/catalog", (ILiveCatalogProvider provider) =>
{
    var catalog = provider.BuildCatalog();
    return Results.Json(catalog);
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

app.MapGet("/api/predictive/status", async (PredictiveStatusProvider provider, CancellationToken ct) =>
{
    var statuses = await provider.GetStatusesAsync(ct).ConfigureAwait(false);
    return Results.Json(statuses);
});

app.MapGet("/api/monitoring/status", (MonitoringStatusProvider provider) =>
{
    var statuses = provider.GetStatuses();
    return Results.Json(statuses);
});

// ---------- Razor Components endpoints ----------
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddInteractiveWebAssemblyRenderMode()
   .AddAdditionalAssemblies(typeof(Home).Assembly);

// Ensure requests to the site root are served the Blazor bootstrapper
// so the router can activate the Home page instead of returning 404.
// app.MapFallbackToFile("apphost.html");

app.Run();

public partial class Program
{
}

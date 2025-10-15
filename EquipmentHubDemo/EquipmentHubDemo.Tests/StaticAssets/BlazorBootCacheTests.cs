using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace EquipmentHubDemo.Tests.StaticAssets;

public sealed class BlazorBootCacheTests : IClassFixture<BlazorBootCacheTests.BootManifestFactory>
{
    private readonly HttpClient _client;

    public BlazorBootCacheTests(BootManifestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task BootManifest_IsServedWithNoCacheHeaders()
    {
        using var response = await _client.GetAsync("/_framework/blazor.boot.json");

        Assert.True(response.Headers.CacheControl?.NoCache, "Cache-Control must declare 'no-cache'.");
        Assert.True(response.Headers.CacheControl?.NoStore, "Cache-Control must declare 'no-store'.");
        Assert.True(response.Headers.CacheControl?.MustRevalidate, "Cache-Control must declare 'must-revalidate'.");

        Assert.True(response.Headers.TryGetValues(HeaderNames.Pragma, out var pragmaValues),
            "Pragma header should be present.");
        Assert.Contains(pragmaValues, value => value.Equals("no-cache", StringComparison.OrdinalIgnoreCase));

        var expiresValues = response.Headers.TryGetValues(HeaderNames.Expires, out var headerExpires)
            ? headerExpires
            : response.Content.Headers.TryGetValues(HeaderNames.Expires, out var contentExpires)
                ? contentExpires
                : Array.Empty<string>();

        Assert.NotEmpty(expiresValues);
        Assert.Contains(expiresValues, value => value.Equals("0", StringComparison.Ordinal));
    }

    public sealed class BootManifestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
            });
        }
    }
}

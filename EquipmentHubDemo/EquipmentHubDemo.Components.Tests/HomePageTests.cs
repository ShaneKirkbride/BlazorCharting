using System.Net;
using System.Text;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class HomePageTests : TestContext
{
    [Fact]
    public void Home_RendersChartIsland_WhenKeysAreAvailable()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        handler.RegisterJsonResponse("http://localhost/api/keys", "[\"Line A\"]");
        handler.RegisterJsonResponse(
            "http://localhost/api/live?key=Line%20A&sinceTicks=0",
            "[{\"x\":\"2024-01-01T00:00:00Z\",\"y\":1.5}]");

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        Services.AddSingleton<HttpClient>(httpClient);
        Services.AddSingleton<NavigationManager>(new StubNavigationManager());

        // Act
        var cut = RenderComponent<Home>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var chartIsland = cut.FindComponent<ChartIsland>();
            Assert.NotNull(chartIsland);

            var statusText = chartIsland.Find("p").TextContent.Trim();
            Assert.Contains("island points", statusText);
        }, timeout: TimeSpan.FromSeconds(1));
    }

    private sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            // Navigation is not required for tests.
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterJsonResponse(string url, string json)
        {
            _responses[url] = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (_responses.TryGetValue(uri, out var json))
            {
                return Task.FromResult(CreateJsonResponse(json));
            }

            if (uri.Contains("/api/live", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse("[]"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage CreateJsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}

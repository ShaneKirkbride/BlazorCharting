using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using EquipmentHubDemo.Domain;
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
        var encodedKey = Uri.EscapeDataString("Line A");
        handler.RegisterJsonResponse(
            $"http://localhost/api/live?key={encodedKey}&sinceTicks=0",
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

    [Fact]
    public void Home_RendersChartIsland_WhenKeysAppearLater()
    {
        // Arrange
        var handler = new StubHttpMessageHandler();
        var encodedKey = Uri.EscapeDataString("Line A");
        handler.RegisterJsonSequence("http://localhost/api/keys", "[]", "[\"Line A\"]");
        handler.RegisterJsonResponse(
            $"http://localhost/api/live?key={encodedKey}&sinceTicks=0",
            "[{\"x\":\"2024-01-01T00:00:00Z\",\"y\":1.5}]");

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        Services.AddSingleton<HttpClient>(httpClient);
        Services.AddSingleton<NavigationManager>(new StubNavigationManager());

        // Act
        var cut = RenderComponent<Home>();

        var totalField = typeof(Home).GetField("_totalReceived", BindingFlags.Instance | BindingFlags.NonPublic);
        var pointsField = typeof(Home).GetField("_points", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(totalField);
        Assert.NotNull(pointsField);

        // Ensure the live endpoint was polled.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(handler.RequestedUris, uri =>
            {
                try
                {
                    var normalized = Uri.UnescapeDataString(uri);
                    return normalized.StartsWith("http://localhost/api/live?key=Line A", StringComparison.Ordinal);
                }
                catch (UriFormatException)
                {
                    return false;
                }
            });
        }, timeout: TimeSpan.FromSeconds(5));

        // Assert - internal state receives live data without manual refresh.
        cut.WaitForAssertion(() =>
        {
            var total = (long)(totalField!.GetValue(cut.Instance) ?? 0L);
            var points = (List<PointDto>?)pointsField!.GetValue(cut.Instance);

            Assert.True(total > 0, "total measurements should increase");
            Assert.NotNull(points);
            Assert.True(points!.Count > 0, "chart points should be populated");
        }, timeout: TimeSpan.FromSeconds(5));
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
        private readonly Dictionary<string, Queue<string>> _responses = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RequestedUris { get; } = new();

        public void RegisterJsonResponse(string url, string json)
        {
            _responses[NormalizeUrl(url)] = new Queue<string>(new[] { json });
        }

        public void RegisterJsonSequence(string url, params string[] json)
        {
            if (json.Length == 0)
            {
                throw new ArgumentException("Sequence must contain at least one entry.", nameof(json));
            }

            _responses[NormalizeUrl(url)] = new Queue<string>(json);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            RequestedUris.Add(uri);
            var lookup = NormalizeUrl(uri);
            if (_responses.TryGetValue(lookup, out var queue))
            {
                var payload = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                return Task.FromResult(CreateJsonResponse(payload));
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

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            try
            {
                return Uri.UnescapeDataString(url);
            }
            catch (UriFormatException)
            {
                return url;
            }
        }
    }
}

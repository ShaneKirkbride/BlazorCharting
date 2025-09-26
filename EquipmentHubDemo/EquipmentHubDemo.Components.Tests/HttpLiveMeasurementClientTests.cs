using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EquipmentHubDemo.Client.Services;
using EquipmentHubDemo.Domain.Live;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class HttpLiveMeasurementClientTests
{
    [Fact]
    public async Task GetAvailableKeysAsync_FallsBackToNextBaseAddress()
    {
        // Arrange
        var handler = new SequenceMessageHandler();
        handler.Register(
            "https://client/api/keys",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>client host</html>", Encoding.UTF8, "text/html")
            });
        handler.Register(
            "https://server/api/keys",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[\"Line A\"]", Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new ApiClientOptions
        {
            BaseAddresses = new List<string> { "https://client", "https://server" }
        });
        var navigation = new TestNavigationManager("https://client/");

        var client = new HttpLiveMeasurementClient(httpClient, options, navigation);

        // Act
        var keys = await client.GetAvailableKeysAsync();

        // Assert
        Assert.Equal(new[] { "Line A" }, keys);
        Assert.Equal(
            new[] { "https://client/api/keys", "https://server/api/keys" },
            handler.RequestedUris);
    }

    [Fact]
    public async Task GetAvailableKeysAsync_WhenNonJson_ThrowsMeaningfulException()
    {
        // Arrange
        var handler = new SequenceMessageHandler();
        handler.Register(
            "https://client/api/keys",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html")
            });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new ApiClientOptions
        {
            BaseAddresses = new List<string> { "https://client" }
        });
        var navigation = new TestNavigationManager("https://client/");

        var client = new HttpLiveMeasurementClient(httpClient, options, navigation);

        // Act
        var ex = await Assert.ThrowsAsync<LiveMeasurementClientException>(() => client.GetAvailableKeysAsync());

        // Assert
        Assert.Contains("Non-JSON payload", ex.Message);
        Assert.DoesNotContain("invalid start of a value", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMeasurementsAsync_ReturnsPoints()
    {
        // Arrange
        var handler = new SequenceMessageHandler();
        handler.Register(
            "https://server/api/live?key=Line%20A&sinceTicks=0",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "[{\"x\":\"2024-01-01T00:00:00Z\",\"y\":2.5}]",
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new ApiClientOptions
        {
            BaseAddresses = new List<string> { "https://server" }
        });
        var navigation = new TestNavigationManager("https://server/");

        var client = new HttpLiveMeasurementClient(httpClient, options, navigation);

        // Act
        var points = await client.GetMeasurementsAsync("Line A", 0);

        // Assert
        var point = Assert.Single(points);
        Assert.Equal(2.5, point.Y);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), point.X);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri)
        {
            Initialize(baseUri, baseUri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            // Navigation is not required for tests.
        }
    }

    private sealed class SequenceMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);
        public List<string> RequestedUris { get; } = new();

        public void Register(string absoluteUrl, HttpResponseMessage response)
        {
            var key = Normalize(absoluteUrl);

            if (!_responses.TryGetValue(key, out var queue))
            {
                queue = new Queue<HttpResponseMessage>();
                _responses[key] = queue;
            }

            queue.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestedUris.Add(uri);

            var key = Normalize(uri);

            if (_responses.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                return Task.FromResult(queue.Dequeue());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new Uri(value, UriKind.Absolute).AbsoluteUri;
        }
    }
}

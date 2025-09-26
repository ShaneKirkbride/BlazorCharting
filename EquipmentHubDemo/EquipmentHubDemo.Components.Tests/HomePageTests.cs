using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Live;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class HomePageTests : TestContext
{
    [Fact]
    public void Home_RendersChartIsland_WhenKeysAreAvailable()
    {
        // Arrange
        var measurementClient = new StubLiveMeasurementClient(
            keysSequence: new[]
            {
                new[] { "Line A" }
            },
            measurementsSequence: new IReadOnlyList<PointDto>[]
            {
                new[] { new PointDto { X = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), Y = 1.5 } },
                Array.Empty<PointDto>()
            });

        Services.AddSingleton<ILiveMeasurementClient>(measurementClient);

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
        var measurementClient = new StubLiveMeasurementClient(
            keysSequence: new IReadOnlyList<string>[]
            {
                Array.Empty<string>(),
                new[] { "Line A" }
            },
            measurementsSequence: new IReadOnlyList<PointDto>[]
            {
                new[] { new PointDto { X = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), Y = 1.5 } },
                Array.Empty<PointDto>()
            });

        Services.AddSingleton<ILiveMeasurementClient>(measurementClient);

        // Act
        var cut = RenderComponent<Home>();

        // Ensure the live endpoint was polled.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(measurementClient.MeasurementRequests, r =>
                string.Equals(r.Key, "Line A", StringComparison.Ordinal));
        }, timeout: TimeSpan.FromSeconds(5));

        // Assert - internal state receives live data without manual refresh.
        cut.WaitForAssertion(() =>
        {
            Assert.True(measurementClient.MeasurementRequests.Count > 0, "should request live measurements");

            var badge = cut.Find("span.badge").TextContent;
            Assert.Contains("Measurements received", badge);
            Assert.Contains("1", badge);

            var chartPoints = cut.FindComponent<ChartIsland>().Instance;
            Assert.NotNull(chartPoints);
        }, timeout: TimeSpan.FromSeconds(5));
    }

    private sealed class StubLiveMeasurementClient : ILiveMeasurementClient
    {
        private readonly Queue<IReadOnlyList<string>> _keys;
        private readonly Queue<IReadOnlyList<PointDto>> _measurements;

        public StubLiveMeasurementClient(
            IEnumerable<IReadOnlyList<string>> keysSequence,
            IEnumerable<IReadOnlyList<PointDto>> measurementsSequence)
        {
            _keys = new Queue<IReadOnlyList<string>>(Clone(keysSequence));
            _measurements = new Queue<IReadOnlyList<PointDto>>(Clone(measurementsSequence));
        }

        public List<(string Key, long SinceTicks)> MeasurementRequests { get; } = new();

        public Task<IReadOnlyList<string>> GetAvailableKeysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_keys.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            var value = _keys.Count > 1 ? _keys.Dequeue() : _keys.Peek();
            return Task.FromResult(value);
        }

        public Task<IReadOnlyList<PointDto>> GetMeasurementsAsync(string key, long sinceTicks, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeasurementRequests.Add((key, sinceTicks));

            if (_measurements.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<PointDto>>(Array.Empty<PointDto>());
            }

            var value = _measurements.Count > 1 ? _measurements.Dequeue() : _measurements.Peek();
            return Task.FromResult(value);
        }

        private static IEnumerable<IReadOnlyList<string>> Clone(IEnumerable<IReadOnlyList<string>> source)
            => source.Select(list => (IReadOnlyList<string>)list.ToArray());

        private static IEnumerable<IReadOnlyList<PointDto>> Clone(IEnumerable<IReadOnlyList<PointDto>> source)
            => source.Select(list => (IReadOnlyList<PointDto>)list.Select(p => new PointDto { X = p.X, Y = p.Y }).ToArray());
    }
}

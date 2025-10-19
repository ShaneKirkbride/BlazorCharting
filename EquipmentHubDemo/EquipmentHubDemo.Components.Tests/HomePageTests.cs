using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Live;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Domain.Status;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class HomePageTests : TestContext
{
    public HomePageTests()
    {
        Services.AddSingleton<ISystemStatusClient>(new StubSystemStatusClient());
    }

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
        var cut = RenderHome();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var chartIsland = cut.FindComponent<ChartIsland>();
            Assert.NotNull(chartIsland);

            var instrumentLabel = cut.Find("h6.chart-card__instrument").TextContent.Trim();
            Assert.Equal("Line A", instrumentLabel);

            var samplesText = cut.Find(".chart-card__stat dd").TextContent.Trim();
            Assert.Equal("No samples", samplesText);
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
        var cut = RenderHome();

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

            var chartPoints = cut.FindComponent<ChartIsland>().Instance;
            Assert.NotNull(chartPoints);
        }, timeout: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Home_RendersUpToNineChartsByDefault()
    {
        // Arrange
        var measurementClient = new StubLiveMeasurementClient(
            keysSequence: new[]
            {
                new[]
                {
                    "Line A", "Line B", "Line C", "Line D", "Line E",
                    "Line F", "Line G", "Line H", "Line I", "Line J", "Line K"
                }
            },
            measurementsSequence: new IReadOnlyList<PointDto>[]
            {
                Array.Empty<PointDto>()
            });

        Services.AddSingleton<ILiveMeasurementClient>(measurementClient);

        // Act
        var cut = RenderHome();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var chartIslands = cut.FindComponents<ChartIsland>();
            Assert.Equal(9, chartIslands.Count);

            var titles = cut.FindAll("h6.chart-card__instrument").Select(e => e.TextContent.Trim()).ToList();
            Assert.Contains("Line A", titles);
            Assert.Contains("Line B", titles);
            Assert.Contains("Line C", titles);
            Assert.Contains("Line I", titles);
            Assert.DoesNotContain("Line J", titles);
        }, timeout: TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Home_PrioritizesPowerAndTemperatureCharts()
    {
        // Arrange
        var measurementClient = new StubLiveMeasurementClient(
            keysSequence: new[]
            {
                new[]
                {
                    "IN-1:Heartbeat",
                    "IN-1:SelfCheck",
                    "IN-1:Vibration",
                    "IN-1:Humidity",
                    "IN-1:Flow",
                    "IN-1:Pressure",
                    "IN-1:Noise",
                    "IN-1:Voltage",
                    "IN-1:Speed",
                    "IN-1:Power (240VAC)",
                    "IN-1:Temperature"
                }
            },
            measurementsSequence: new IReadOnlyList<PointDto>[]
            {
                Array.Empty<PointDto>()
            });

        Services.AddSingleton<ILiveMeasurementClient>(measurementClient);

        // Act
        var cut = RenderHome();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var instruments = cut.FindAll("h6.chart-card__instrument")
                .Select(e => e.TextContent.Trim())
                .ToList();
            var metrics = cut.FindAll(".chart-card__metric")
                .Select(e => e.TextContent.Trim())
                .ToList();

            Assert.Contains("IN-1", instruments);
            Assert.Contains("Power (240VAC)", metrics);
            Assert.Contains("Temperature", metrics);
            Assert.Equal(9, instruments.Count);
            Assert.Equal(9, metrics.Count);
        }, timeout: TimeSpan.FromSeconds(2));
    }

    private IRenderedComponent<Home> RenderHome()
        => RenderComponent<Home>(parameters => parameters.Add(p => p.ForceEnableLiveCharts, true));

    private sealed class StubLiveMeasurementClient : ILiveMeasurementClient
    {
        private readonly Queue<IReadOnlyList<string>> _keys;
        private readonly Queue<IReadOnlyList<PointDto>> _measurements;
        private IReadOnlyList<string> _lastCatalogKeys = Array.Empty<string>();

        public StubLiveMeasurementClient(
            IEnumerable<IReadOnlyList<string>> keysSequence,
            IEnumerable<IReadOnlyList<PointDto>> measurementsSequence)
        {
            _keys = new Queue<IReadOnlyList<string>>(Clone(keysSequence));
            _measurements = new Queue<IReadOnlyList<PointDto>>(Clone(measurementsSequence));
        }

        public List<(string Key, long SinceTicks)> MeasurementRequests { get; } = new();

        public Task<MeasurementCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = DequeueKeysIfAvailable();
            _lastCatalogKeys = snapshot;
            return Task.FromResult(BuildCatalog(snapshot));
        }

        public Task<IReadOnlyList<string>> GetAvailableKeysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_lastCatalogKeys.Count == 0)
            {
                var snapshot = DequeueKeysIfAvailable();
                _lastCatalogKeys = snapshot;
            }

            return Task.FromResult(_lastCatalogKeys);
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

        private IReadOnlyList<string> DequeueKeysIfAvailable()
        {
            if (_keys.Count == 0)
            {
                return Array.Empty<string>();
            }

            var source = _keys.Count > 1 ? _keys.Dequeue() : _keys.Peek();
            return (IReadOnlyList<string>)source.ToArray();
        }

        private static MeasurementCatalog BuildCatalog(IReadOnlyList<string> keys)
        {
            if (keys.Count == 0)
            {
                return MeasurementCatalog.Empty;
            }

            var instruments = new Dictionary<string, List<MetricSlice>>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                var instrumentId = "Ungrouped";
                var metricName = key;
                if (MeasureKey.TryParse(key, out var parsed))
                {
                    instrumentId = string.IsNullOrWhiteSpace(parsed.InstrumentId) ? "Ungrouped" : parsed.InstrumentId;
                    metricName = string.IsNullOrWhiteSpace(parsed.Metric) ? key : parsed.Metric;
                }

                if (!instruments.TryGetValue(instrumentId, out var metrics))
                {
                    metrics = new List<MetricSlice>();
                    instruments[instrumentId] = metrics;
                }

                metrics.Add(new MetricSlice
                {
                    Key = key,
                    DisplayName = metricName,
                    Metric = metricName,
                    IsPreferred = LiveCatalogPreferences.IsPreferredMetric(metricName)
                });
            }

            var instrumentSlices = instruments
                .Select(pair => new InstrumentSlice
                {
                    Id = pair.Key,
                    DisplayName = pair.Key,
                    Metrics = pair.Value
                        .OrderByDescending(metric => metric.IsPreferred)
                        .ThenBy(metric => metric.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .OrderBy(instrument => instrument.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new MeasurementCatalog
            {
                Instruments = instrumentSlices
            };
        }

        private static IEnumerable<IReadOnlyList<string>> Clone(IEnumerable<IReadOnlyList<string>> source)
            => source.Select(list => (IReadOnlyList<string>)list.ToArray());

        private static IEnumerable<IReadOnlyList<PointDto>> Clone(IEnumerable<IReadOnlyList<PointDto>> source)
            => source.Select(list => (IReadOnlyList<PointDto>)list.Select(p => new PointDto { X = p.X, Y = p.Y }).ToArray());
    }

    private sealed class StubSystemStatusClient : ISystemStatusClient
    {
        public Task<IReadOnlyList<PredictiveStatus>> GetPredictiveStatusesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PredictiveStatus>>(Array.Empty<PredictiveStatus>());

        public Task<IReadOnlyList<MonitoringStatus>> GetMonitoringStatusesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MonitoringStatus>>(Array.Empty<MonitoringStatus>());
    }
}

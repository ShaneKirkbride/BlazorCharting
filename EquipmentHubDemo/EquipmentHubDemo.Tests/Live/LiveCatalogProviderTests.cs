using System;
using System.Linq;
using EquipmentHubDemo.Domain.Live;
using EquipmentHubDemo.Live;

namespace EquipmentHubDemo.Tests.Live;

public sealed class LiveCatalogProviderTests
{
    [Fact]
    public void BuildCatalog_ReturnsEmptyWhenCacheEmpty()
    {
        var provider = new LiveCatalogProvider(new StubLiveCache());

        var catalog = provider.BuildCatalog();

        Assert.Same(MeasurementCatalog.Empty, catalog);
    }

    [Fact]
    public void BuildCatalog_GroupsMetricsByInstrument()
    {
        var cache = new StubLiveCache(
            "UXG-01:Temperature",
            "UXG-01:Power (240VAC)",
            "SG-20:Humidity",
            "SG-20:Voltage");
        var provider = new LiveCatalogProvider(cache);

        var catalog = provider.BuildCatalog();

        Assert.Equal(2, catalog.Instruments.Count);
        var first = catalog.Instruments[0];
        Assert.Equal("SG-20", first.Id);
        Assert.Equal("Sg 20", first.DisplayName);
        Assert.Collection(first.Metrics,
            metric =>
            {
                Assert.Equal("SG-20:Humidity", metric.Key);
                Assert.Equal("Humidity", metric.DisplayName);
                Assert.Equal("Humidity", metric.Metric);
                Assert.False(metric.IsPreferred);
            },
            metric =>
            {
                Assert.Equal("SG-20:Voltage", metric.Key);
                Assert.Equal("Voltage", metric.DisplayName);
                Assert.Equal("Voltage", metric.Metric);
                Assert.False(metric.IsPreferred);
            });

        var second = catalog.Instruments[1];
        Assert.Equal("UXG-01", second.Id);
        Assert.Equal("Uxg 01", second.DisplayName);
        Assert.Collection(second.Metrics,
            metric =>
            {
                Assert.Equal("UXG-01:Power (240VAC)", metric.Key);
                Assert.Equal("Power (240VAC)", metric.DisplayName);
                Assert.Equal("Power (240VAC)", metric.Metric);
                Assert.True(metric.IsPreferred);
            },
            metric =>
            {
                Assert.Equal("UXG-01:Temperature", metric.Key);
                Assert.Equal("Temperature", metric.DisplayName);
                Assert.Equal("Temperature", metric.Metric);
                Assert.True(metric.IsPreferred);
            });
    }

    [Fact]
    public void BuildCatalog_HandlesMalformedKeys()
    {
        var cache = new StubLiveCache("", "bad-key", "InstrumentOnly:");
        var provider = new LiveCatalogProvider(cache);

        var catalog = provider.BuildCatalog();

        Assert.Equal(2, catalog.Instruments.Count);

        var instrumentWithId = catalog.Instruments.Single(slice => slice.Id == "InstrumentOnly");
        Assert.Collection(instrumentWithId.Metrics,
            metric =>
            {
                Assert.Equal("InstrumentOnly:", metric.Key);
                Assert.Equal("Telemetry", metric.DisplayName);
                Assert.Equal(string.Empty, metric.Metric);
            });

        var ungrouped = catalog.Instruments.Single(slice => slice.Id == "Ungrouped");
        Assert.Collection(ungrouped.Metrics,
            metric =>
            {
                Assert.Equal("bad-key", metric.Key);
                Assert.Equal("bad-key", metric.DisplayName);
            });
    }

    private sealed class StubLiveCache : ILiveCache
    {
        private readonly IReadOnlyList<string> _keys;

        public StubLiveCache(params string[] keys) => _keys = keys;

        public event Action? Updated
        {
            add { }
            remove { }
        }

        public IReadOnlyList<string> Keys => _keys;

        public IReadOnlyList<(DateTime X, double Y)> GetSeries(string key) => Array.Empty<(DateTime, double)>();

        public void Push(string key, DateTime x, double y)
            => throw new NotSupportedException();
    }
}

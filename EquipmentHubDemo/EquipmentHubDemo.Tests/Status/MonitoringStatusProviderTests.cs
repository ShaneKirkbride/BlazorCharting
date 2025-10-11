using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Live;
using EquipmentHubDemo.Status;
using EquipmentHubDemo.Tests;

namespace EquipmentHubDemo.Tests.Status;

public sealed class MonitoringStatusProviderTests
{
    [Fact]
    public void GetStatuses_AssignsHealthBasedOnFreshness()
    {
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var cache = new TestLiveCache("IN-1:Heartbeat", "IN-1:SelfCheck", "IN-2:Humidity", "IN-1:Power");
        cache.Push("IN-1:Heartbeat", now.UtcDateTime.AddSeconds(-30), 1);
        cache.Push("IN-1:SelfCheck", now.UtcDateTime.AddMinutes(-20), 1);
        cache.SetSeries("IN-2:Humidity", Array.Empty<(DateTime, double)>());
        cache.Push("IN-1:Power", now.UtcDateTime.AddSeconds(-10), 200);

        var provider = new MonitoringStatusProvider(cache, new TestTimeProvider(now));

        var statuses = provider.GetStatuses();

        Assert.Equal(3, statuses.Count);

        var heartbeat = Assert.Single(statuses, s => s.Metric == "Heartbeat");
        Assert.Equal(MonitoringHealth.Nominal, heartbeat.Health);
        Assert.Equal(1, heartbeat.LastValue);
        Assert.Equal(now.UtcDateTime.AddSeconds(-30), heartbeat.LastObservedUtc);

        var selfCheck = Assert.Single(statuses, s => s.Metric == "SelfCheck");
        Assert.Equal(MonitoringHealth.Stale, selfCheck.Health);
        Assert.Equal(now.UtcDateTime.AddMinutes(-20), selfCheck.LastObservedUtc);

        var humidity = Assert.Single(statuses, s => s.Metric == "Humidity");
        Assert.Equal(MonitoringHealth.Missing, humidity.Health);
        Assert.Null(humidity.LastObservedUtc);
        Assert.Null(humidity.LastValue);

        Assert.DoesNotContain(statuses, s => s.Metric.Contains("Power", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetStatuses_WhenNoMetrics_ReturnsEmpty()
    {
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var cache = new TestLiveCache();
        var provider = new MonitoringStatusProvider(cache, new TestTimeProvider(now));

        var statuses = provider.GetStatuses();

        Assert.Empty(statuses);
    }

    [Fact]
    public void GetStatuses_FutureMeasurementsClampAgeToZero()
    {
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var cache = new TestLiveCache("IN-3:Temperature");
        cache.Push("IN-3:Temperature", now.UtcDateTime.AddMinutes(1), 21.5);

        var provider = new MonitoringStatusProvider(cache, new TestTimeProvider(now));

        var statuses = provider.GetStatuses();

        var temperature = Assert.Single(statuses);
        Assert.Equal(TimeSpan.Zero, temperature.Age);
        Assert.Equal(MonitoringHealth.Nominal, temperature.Health);
    }
}

internal sealed class TestLiveCache : ILiveCache
{
    private readonly Dictionary<string, List<(DateTime X, double Y)>> _series = new(StringComparer.Ordinal);

    public TestLiveCache(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!_series.ContainsKey(key))
            {
                _series[key] = new List<(DateTime, double)>();
            }
        }
    }

    public event Action? Updated
    {
        add { }
        remove { }
    }

    public IReadOnlyList<string> Keys => _series.Keys.ToList();

    public IReadOnlyList<(DateTime X, double Y)> GetSeries(string key)
        => _series.TryGetValue(key, out var points) ? points : new List<(DateTime, double)>();

    public void Push(string key, DateTime x, double y)
    {
        if (!_series.TryGetValue(key, out var points))
        {
            points = new List<(DateTime, double)>();
            _series[key] = points;
        }

        points.Add((x, y));
    }

    public void SetSeries(string key, IEnumerable<(DateTime X, double Y)> points)
    {
        _series[key] = points.ToList();
    }
}

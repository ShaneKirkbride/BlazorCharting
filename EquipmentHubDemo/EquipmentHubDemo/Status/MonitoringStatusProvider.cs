using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Live;

namespace EquipmentHubDemo.Status;

public sealed class MonitoringStatusProvider
{
    private static readonly IReadOnlyDictionary<string, TimeSpan> FreshnessThresholds =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["Heartbeat"] = TimeSpan.FromSeconds(90),
            ["SelfCheck"] = TimeSpan.FromMinutes(15),
            ["Temperature"] = TimeSpan.FromMinutes(3),
            ["Humidity"] = TimeSpan.FromMinutes(3)
        };

    private readonly ILiveCache _liveCache;
    private readonly TimeProvider _timeProvider;

    public MonitoringStatusProvider(ILiveCache liveCache, TimeProvider timeProvider)
    {
        _liveCache = liveCache ?? throw new ArgumentNullException(nameof(liveCache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public IReadOnlyList<MonitoringStatus> GetStatuses()
    {
        var keys = _liveCache.Keys;
        if (keys.Count == 0)
        {
            return Array.Empty<MonitoringStatus>();
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var statuses = new List<MonitoringStatus>();

        foreach (var key in keys)
        {
            if (!MeasureKey.TryParse(key, out var parsed))
            {
                continue;
            }

            if (!FreshnessThresholds.TryGetValue(parsed.Metric, out var threshold))
            {
                continue;
            }

            var series = _liveCache.GetSeries(key);
            statuses.Add(BuildStatus(parsed, series, now, threshold));
        }

        return statuses
            .OrderBy(status => status.InstrumentId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Metric, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MonitoringStatus BuildStatus(
        MeasureKey key,
        IReadOnlyList<(DateTime X, double Y)> series,
        DateTime now,
        TimeSpan threshold)
    {
        if (series.Count == 0)
        {
            return new MonitoringStatus(key.InstrumentId, key.Metric, null, null, null, MonitoringHealth.Missing);
        }

        var last = series[^1];
        var age = now - last.X;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var health = age <= threshold ? MonitoringHealth.Nominal : MonitoringHealth.Stale;
        return new MonitoringStatus(key.InstrumentId, key.Metric, last.X, last.Y, age, health);
    }
}

using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Live;
using EquipmentHubDemo.Status;

namespace EquipmentHubDemo.Tests.Status;

public sealed class PredictiveStatusProviderTests
{
    [Fact]
    public async Task GetStatusesAsync_FiltersNonPredictiveMetricsAndOrdersResults()
    {
        var cache = new PredictiveTestLiveCache("B:Humidity", "A:Temperature", "A:Power (240VAC)");
        var maintenance = new StubPredictiveMaintenanceService();
        var now = DateTime.UtcNow;

        maintenance.SetSummary(new PredictiveMaintenanceSummary(
            new PredictiveInsight("A", "Temperature", 0.1, now, 10, 1),
            new MaintenancePlan("A", "Temperature", "Service", now.AddDays(12), "Service notes"),
            new MaintenancePlan("A", "Temperature", "Repair", now.AddDays(5), "Repair notes")));

        maintenance.SetSummary(new PredictiveMaintenanceSummary(
            new PredictiveInsight("B", "Humidity", 0.2, now, 55, 3),
            new MaintenancePlan("B", "Humidity", "Service", now.AddDays(11), "Service"),
            new MaintenancePlan("B", "Humidity", "Repair", now.AddDays(6), "Repair")));

        var provider = new PredictiveStatusProvider(cache, maintenance);

        var statuses = await provider.GetStatusesAsync(CancellationToken.None);

        Assert.Equal(2, statuses.Count);
        Assert.Collection(statuses,
            first =>
            {
                Assert.Equal("A", first.InstrumentId);
                Assert.Equal("Temperature", first.Metric);
            },
            second =>
            {
                Assert.Equal("B", second.InstrumentId);
                Assert.Equal("Humidity", second.Metric);
            });

        Assert.DoesNotContain(maintenance.Requested, request => request.Metric.Contains("Power", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetStatusesAsync_ReturnsEmptyWhenNoPredictiveMetrics()
    {
        var cache = new PredictiveTestLiveCache("A:Voltage", "B:Power");
        var maintenance = new StubPredictiveMaintenanceService();
        var provider = new PredictiveStatusProvider(cache, maintenance);

        var statuses = await provider.GetStatusesAsync(CancellationToken.None);

        Assert.Empty(statuses);
        Assert.Empty(maintenance.Requested);
    }
}

internal sealed class StubPredictiveMaintenanceService : IPredictiveMaintenanceService
{
    private readonly Dictionary<(string InstrumentId, string Metric), PredictiveMaintenanceSummary> _summaries = new();

    public List<(string InstrumentId, string Metric)> Requested { get; } = new();

    public void SetSummary(PredictiveMaintenanceSummary summary)
        => _summaries[(summary.Insight.InstrumentId, summary.Insight.Metric)] = summary;

    public Task<PredictiveMaintenanceSummary> GetSummaryAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        Requested.Add((instrumentId, metric));
        if (_summaries.TryGetValue((instrumentId, metric), out var summary))
        {
            return Task.FromResult(summary);
        }

        throw new InvalidOperationException($"No summary registered for {instrumentId}/{metric}.");
    }

    public Task<MaintenancePlan> ScheduleServiceAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<MaintenancePlan> ScheduleRepairAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

internal sealed class PredictiveTestLiveCache : ILiveCache
{
    private readonly Dictionary<string, List<(DateTime X, double Y)>> _series;

    public PredictiveTestLiveCache(params string[] keys)
    {
        _series = keys.ToDictionary(k => k, k => new List<(DateTime, double)>(), StringComparer.Ordinal);
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
}

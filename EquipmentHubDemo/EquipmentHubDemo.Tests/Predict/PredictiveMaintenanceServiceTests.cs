using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Infrastructure.Predict;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquipmentHubDemo.Tests.Predict;

public sealed class PredictiveMaintenanceServiceTests
{
    [Fact]
    public async Task ScheduleServiceAsync_UsesPredictiveInsight()
    {
        var diagnostics = new StubPredictiveDiagnosticsService();
        diagnostics.SetInsight(new PredictiveInsight("IN-500", "Temperature", 0.4, DateTime.UtcNow, 10, 2));
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var service = new PredictiveMaintenanceService(diagnostics, new TestTimeProvider(now), NullLogger<PredictiveMaintenanceService>.Instance);

        var plan = await service.ScheduleServiceAsync("IN-500", "Temperature", CancellationToken.None);

        Assert.Equal("IN-500", plan.InstrumentId);
        Assert.Equal("Service", plan.Action);
        Assert.Equal(now.UtcDateTime.AddDays(10), plan.ScheduledFor);
        Assert.Contains("Failure probability 40.0", plan.Notes);
    }

    [Fact]
    public async Task ScheduleRepairAsync_ComputesUrgency()
    {
        var diagnostics = new StubPredictiveDiagnosticsService();
        diagnostics.SetInsight(new PredictiveInsight("IN-600", "Humidity", 0.8, DateTime.UtcNow, 30, 5));
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var service = new PredictiveMaintenanceService(diagnostics, new TestTimeProvider(now), NullLogger<PredictiveMaintenanceService>.Instance);

        var plan = await service.ScheduleRepairAsync("IN-600", "Humidity", CancellationToken.None);

        Assert.Equal("Repair", plan.Action);
        Assert.Equal(now.UtcDateTime.AddDays(2), plan.ScheduledFor);
        Assert.Contains("μ=30.00", plan.Notes);
        Assert.Contains("σ=5.00", plan.Notes);
    }
}

internal sealed class StubPredictiveDiagnosticsService : IPredictiveDiagnosticsService
{
    private readonly Dictionary<(string InstrumentId, string Metric), PredictiveInsight> _insights = new();

    public void SetInsight(PredictiveInsight insight)
        => _insights[(insight.InstrumentId, insight.Metric)] = insight;

    public Task<PredictiveInsight> GetInsightAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        if (_insights.TryGetValue((instrumentId, metric), out var insight))
        {
            return Task.FromResult(insight);
        }

        throw new InvalidOperationException($"No insight registered for {instrumentId}/{metric}.");
    }
}

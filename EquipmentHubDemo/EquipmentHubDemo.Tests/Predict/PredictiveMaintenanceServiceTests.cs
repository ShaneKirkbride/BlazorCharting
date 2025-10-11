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

    [Fact]
    public async Task ScheduleServiceAsync_ClampsLeadTimeToValidRange()
    {
        var diagnostics = new StubPredictiveDiagnosticsService();
        diagnostics.SetInsight(new PredictiveInsight("IN-601", "Humidity", 1.5, DateTime.UtcNow, 20, 2));
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var service = new PredictiveMaintenanceService(diagnostics, new TestTimeProvider(now), NullLogger<PredictiveMaintenanceService>.Instance);

        var plan = await service.ScheduleServiceAsync("IN-601", "Humidity", CancellationToken.None);

        Assert.Equal(now.UtcDateTime.AddDays(1), plan.ScheduledFor);

        diagnostics.SetInsight(new PredictiveInsight("IN-601", "Humidity", -5, DateTime.UtcNow, 20, 2));
        plan = await service.ScheduleServiceAsync("IN-601", "Humidity", CancellationToken.None);

        Assert.Equal(now.UtcDateTime.AddDays(30), plan.ScheduledFor);
    }

    [Fact]
    public async Task ScheduleRepairAsync_ClampsUrgencyWindow()
    {
        var diagnostics = new StubPredictiveDiagnosticsService();
        diagnostics.SetInsight(new PredictiveInsight("IN-602", "Temperature", 2, DateTime.UtcNow, 15, 1));
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var service = new PredictiveMaintenanceService(diagnostics, new TestTimeProvider(now), NullLogger<PredictiveMaintenanceService>.Instance);

        var plan = await service.ScheduleRepairAsync("IN-602", "Temperature", CancellationToken.None);

        Assert.Equal(now.UtcDateTime.AddDays(1), plan.ScheduledFor);

        diagnostics.SetInsight(new PredictiveInsight("IN-602", "Temperature", -3, DateTime.UtcNow, 15, 1));
        plan = await service.ScheduleRepairAsync("IN-602", "Temperature", CancellationToken.None);

        Assert.Equal(now.UtcDateTime.AddDays(14), plan.ScheduledFor);
    }

    [Fact]
    public async Task GetSummaryAsync_ComputesPlansAndCachesInsight()
    {
        var diagnostics = new StubPredictiveDiagnosticsService();
        var insightTimestamp = new DateTime(2024, 11, 5, 8, 0, 0, DateTimeKind.Utc);
        diagnostics.SetInsight(new PredictiveInsight("IN-700", "Temperature", 0.25, insightTimestamp, 15, 1.5));
        var now = new DateTimeOffset(2024, 11, 05, 12, 0, 0, TimeSpan.Zero);
        var service = new PredictiveMaintenanceService(diagnostics, new TestTimeProvider(now), NullLogger<PredictiveMaintenanceService>.Instance);

        var summary = await service.GetSummaryAsync("IN-700", "Temperature", CancellationToken.None);

        Assert.Equal(1, diagnostics.CallCount);
        Assert.Equal(now.UtcDateTime.AddDays(12), summary.ServicePlan.ScheduledFor);
        Assert.Equal(now.UtcDateTime.AddDays(6), summary.RepairPlan.ScheduledFor);
        Assert.Same(summary.Insight, diagnostics.GetInsight("IN-700", "Temperature"));
    }
}

internal sealed class StubPredictiveDiagnosticsService : IPredictiveDiagnosticsService
{
    private readonly Dictionary<(string InstrumentId, string Metric), PredictiveInsight> _insights = new();

    public int CallCount { get; private set; }

    public void SetInsight(PredictiveInsight insight)
        => _insights[(insight.InstrumentId, insight.Metric)] = insight;

    public Task<PredictiveInsight> GetInsightAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (_insights.TryGetValue((instrumentId, metric), out var insight))
        {
            return Task.FromResult(insight);
        }

        throw new InvalidOperationException($"No insight registered for {instrumentId}/{metric}.");
    }

    public PredictiveInsight GetInsight(string instrumentId, string metric) => _insights[(instrumentId, metric)];
}

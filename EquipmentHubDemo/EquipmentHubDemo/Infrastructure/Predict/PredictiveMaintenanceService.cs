using EquipmentHubDemo.Domain.Predict;
using Microsoft.Extensions.Logging;

namespace EquipmentHubDemo.Infrastructure.Predict;

public sealed class PredictiveMaintenanceService : IPredictiveMaintenanceService
{
    private readonly IPredictiveDiagnosticsService _diagnosticsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PredictiveMaintenanceService> _logger;

    public PredictiveMaintenanceService(
        IPredictiveDiagnosticsService diagnosticsService,
        TimeProvider timeProvider,
        ILogger<PredictiveMaintenanceService> logger)
    {
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PredictiveMaintenanceSummary> GetSummaryAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);

        var insight = await _diagnosticsService.GetInsightAsync(instrumentId, metric, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var servicePlan = CreateServicePlan(insight, now);
        var repairPlan = CreateRepairPlan(insight, now);

        return new PredictiveMaintenanceSummary(insight, servicePlan, repairPlan);
    }

    public async Task<MaintenancePlan> ScheduleServiceAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        var summary = await GetSummaryAsync(instrumentId, metric, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Scheduled predictive service for {Instrument}/{Metric} on {Date}.",
            instrumentId,
            metric,
            summary.ServicePlan.ScheduledFor);

        return summary.ServicePlan;
    }

    public async Task<MaintenancePlan> ScheduleRepairAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        var summary = await GetSummaryAsync(instrumentId, metric, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Scheduled predictive repair for {Instrument}/{Metric} on {Date}.",
            instrumentId,
            metric,
            summary.RepairPlan.ScheduledFor);

        return summary.RepairPlan;
    }

    private static MaintenancePlan CreateServicePlan(PredictiveInsight insight, DateTime now)
    {
        var daysUntilService = Math.Clamp(14 - (int)Math.Round(insight.FailureProbability * 10), 1, 30);
        var scheduled = now.AddDays(daysUntilService);
        var notes = BuildNotes(insight, "service");
        return new MaintenancePlan(insight.InstrumentId, insight.Metric, "Service", scheduled, notes);
    }

    private static MaintenancePlan CreateRepairPlan(PredictiveInsight insight, DateTime now)
    {
        var urgency = insight.FailureProbability;
        var daysUntilRepair = Math.Clamp((int)Math.Ceiling(7 * (1 - urgency)), 1, 14);
        var scheduled = now.AddDays(daysUntilRepair);
        var notes = BuildNotes(insight, "repair");
        return new MaintenancePlan(insight.InstrumentId, insight.Metric, "Repair", scheduled, notes);
    }

    private static string BuildNotes(PredictiveInsight insight, string action)
        => $"Predictive {action} scheduled. Failure probability {insight.FailureProbability:P1}, μ={insight.Mean:F2}, σ={insight.StandardDeviation:F2}.";
}

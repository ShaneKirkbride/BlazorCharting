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

    public async Task<MaintenancePlan> ScheduleServiceAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        var insight = await _diagnosticsService.GetInsightAsync(instrumentId, metric, cancellationToken).ConfigureAwait(false);
        var daysUntilService = Math.Clamp(14 - (int)Math.Round(insight.FailureProbability * 10), 1, 30);
        var scheduled = _timeProvider.GetUtcNow().UtcDateTime.AddDays(daysUntilService);
        var notes = BuildNotes(insight, "service");

        _logger.LogInformation("Scheduled predictive service for {Instrument}/{Metric} on {Date}.", instrumentId, metric, scheduled);
        return new MaintenancePlan(instrumentId, metric, "Service", scheduled, notes);
    }

    public async Task<MaintenancePlan> ScheduleRepairAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        var insight = await _diagnosticsService.GetInsightAsync(instrumentId, metric, cancellationToken).ConfigureAwait(false);
        var urgency = insight.FailureProbability;
        var daysUntilRepair = Math.Clamp((int)Math.Ceiling(7 * (1 - urgency)), 1, 14);
        var scheduled = _timeProvider.GetUtcNow().UtcDateTime.AddDays(daysUntilRepair);
        var notes = BuildNotes(insight, "repair");

        _logger.LogInformation("Scheduled predictive repair for {Instrument}/{Metric} on {Date}.", instrumentId, metric, scheduled);
        return new MaintenancePlan(instrumentId, metric, "Repair", scheduled, notes);
    }

    private static string BuildNotes(PredictiveInsight insight, string action)
        => $"Predictive {action} scheduled. Failure probability {insight.FailureProbability:P1}, μ={insight.Mean:F2}, σ={insight.StandardDeviation:F2}.";
}

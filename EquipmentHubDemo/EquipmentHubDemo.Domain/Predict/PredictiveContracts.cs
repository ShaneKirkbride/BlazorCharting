namespace EquipmentHubDemo.Domain.Predict;

/// <summary>
/// Diagnostic measurement that will be used for predictive analytics.
/// </summary>
/// <param name="InstrumentId">Instrument identifier.</param>
/// <param name="Metric">Metric name (e.g. Temperature, Humidity).</param>
/// <param name="Value">Measured value.</param>
/// <param name="TimestampUtc">Timestamp in UTC.</param>
public sealed record DiagnosticSample(string InstrumentId, string Metric, double Value, DateTime TimestampUtc);

public interface IDiagnosticRepository
{
    Task AddAsync(DiagnosticSample sample, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiagnosticSample>> GetRecentAsync(string instrumentId, string metric, TimeSpan lookback, CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistical insight derived from recent diagnostics.
/// </summary>
/// <param name="InstrumentId">Instrument identifier.</param>
/// <param name="Metric">Metric name.</param>
/// <param name="FailureProbability">Estimated probability of failure in the near term.</param>
/// <param name="TimestampUtc">Timestamp when the insight was generated.</param>
/// <param name="Mean">Mean value of the underlying samples.</param>
/// <param name="StandardDeviation">Standard deviation of the samples.</param>
public sealed record PredictiveInsight(
    string InstrumentId,
    string Metric,
    double FailureProbability,
    DateTime TimestampUtc,
    double Mean,
    double StandardDeviation);

public interface IPredictiveDiagnosticsService
{
    Task<PredictiveInsight> GetInsightAsync(string instrumentId, string metric, CancellationToken cancellationToken = default);
}

/// <summary>
/// Plan describing proactive maintenance or repair.
/// </summary>
/// <param name="InstrumentId">Instrument identifier.</param>
/// <param name="Metric">Metric driving the maintenance action.</param>
/// <param name="Action">Action description (Service or Repair).</param>
/// <param name="ScheduledFor">When the action should be executed.</param>
/// <param name="Notes">Additional notes for the maintenance crew.</param>
public sealed record MaintenancePlan(
    string InstrumentId,
    string Metric,
    string Action,
    DateTime ScheduledFor,
    string Notes);

public interface IPredictiveMaintenanceService
{
    Task<PredictiveMaintenanceSummary> GetSummaryAsync(string instrumentId, string metric, CancellationToken cancellationToken = default);

    Task<MaintenancePlan> ScheduleServiceAsync(string instrumentId, string metric, CancellationToken cancellationToken = default);

    Task<MaintenancePlan> ScheduleRepairAsync(string instrumentId, string metric, CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated predictive maintenance insight including recommended service and repair plans.
/// </summary>
/// <param name="Insight">Latest predictive insight.</param>
/// <param name="ServicePlan">Recommended service plan.</param>
/// <param name="RepairPlan">Recommended repair plan.</param>
public sealed record PredictiveMaintenanceSummary(
    PredictiveInsight Insight,
    MaintenancePlan ServicePlan,
    MaintenancePlan RepairPlan);

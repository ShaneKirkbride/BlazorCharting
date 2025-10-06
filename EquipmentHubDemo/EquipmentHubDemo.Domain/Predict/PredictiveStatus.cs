namespace EquipmentHubDemo.Domain.Predict;

/// <summary>
/// Represents the predictive maintenance state for an instrument metric.
/// </summary>
/// <param name="InstrumentId">Instrument identifier.</param>
/// <param name="Metric">Metric name.</param>
/// <param name="Insight">Latest predictive insight computed for the metric.</param>
/// <param name="ServicePlan">Recommended service plan calculated from the insight.</param>
/// <param name="RepairPlan">Recommended repair plan calculated from the insight.</param>
public sealed record PredictiveStatus(
    string InstrumentId,
    string Metric,
    PredictiveInsight Insight,
    MaintenancePlan ServicePlan,
    MaintenancePlan RepairPlan);

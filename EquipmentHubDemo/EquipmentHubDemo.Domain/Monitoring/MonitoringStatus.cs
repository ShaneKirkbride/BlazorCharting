namespace EquipmentHubDemo.Domain.Monitoring;

public enum MonitoringHealth
{
    Missing = 0,
    Stale = 1,
    Nominal = 2
}

/// <summary>
/// Represents the freshness of monitoring telemetry for an instrument metric.
/// </summary>
/// <param name="InstrumentId">Instrument identifier.</param>
/// <param name="Metric">Metric name.</param>
/// <param name="LastObservedUtc">Timestamp of the most recent reading, if any.</param>
/// <param name="LastValue">Value reported by the most recent reading, if any.</param>
/// <param name="Age">How long ago the most recent reading arrived.</param>
/// <param name="Health">Derived health classification.</param>
public sealed record MonitoringStatus(
    string InstrumentId,
    string Metric,
    DateTime? LastObservedUtc,
    double? LastValue,
    TimeSpan? Age,
    MonitoringHealth Health);

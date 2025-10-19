using System;
using System.Collections.Generic;
using System.Linq;

namespace EquipmentHubDemo.Domain.Live;

/// <summary>
/// Represents the available live measurement catalog grouped by instrument.
/// </summary>
public sealed record class MeasurementCatalog
{
    /// <summary>
    /// An empty catalog instance.
    /// </summary>
    public static MeasurementCatalog Empty { get; } = new();

    /// <summary>
    /// Gets the instruments that currently publish telemetry.
    /// </summary>
    public IReadOnlyList<InstrumentSlice> Instruments { get; init; } = Array.Empty<InstrumentSlice>();

    /// <summary>
    /// Returns all measurement keys contained in the catalog.
    /// </summary>
    public IReadOnlyList<string> GetAllKeys()
    {
        if (Instruments.Count == 0)
        {
            return Array.Empty<string>();
        }

        return Instruments.SelectMany(instrument => instrument.Metrics)
            .Where(metric => !string.IsNullOrWhiteSpace(metric.Key))
            .Select(metric => metric.Key)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>
/// Represents a collection of metrics associated with an instrument.
/// </summary>
public sealed record class InstrumentSlice
{
    /// <summary>
    /// Gets the instrument identifier (stable key).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the user friendly instrument display name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the metrics exposed by the instrument.
    /// </summary>
    public IReadOnlyList<MetricSlice> Metrics { get; init; } = Array.Empty<MetricSlice>();
}

/// <summary>
/// Represents a metric selectable from the slicer UI.
/// </summary>
public sealed record class MetricSlice
{
    /// <summary>
    /// Gets the measurement key suitable for live data queries.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the user friendly metric name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the metric identifier portion of the measurement key.
    /// </summary>
    public string Metric { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this metric should be highlighted as recommended.
    /// </summary>
    public bool IsPreferred { get; init; }
}

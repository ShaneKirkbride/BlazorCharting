using System;
using System.Collections.Generic;

namespace EquipmentHubDemo.Domain.Live;

/// <summary>
/// Provides reusable preferences for live catalog presentation.
/// </summary>
public static class LiveCatalogPreferences
{
    private static readonly string[] PreferredMetricNames =
    {
        "Power (240VAC)",
        "Temperature"
    };

    /// <summary>
    /// Gets the metrics that should be emphasized in slicer views.
    /// </summary>
    public static IReadOnlyList<string> PreferredMetrics => PreferredMetricNames;

    /// <summary>
    /// Determines whether a metric should be surfaced as preferred.
    /// </summary>
    /// <param name="metric">The metric identifier to evaluate.</param>
    /// <returns><c>true</c> when the metric is preferred; otherwise, <c>false</c>.</returns>
    public static bool IsPreferredMetric(string? metric)
    {
        if (string.IsNullOrWhiteSpace(metric))
        {
            return false;
        }

        foreach (var candidate in PreferredMetricNames)
        {
            if (string.Equals(candidate, metric, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

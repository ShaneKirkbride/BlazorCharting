using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Live;

namespace EquipmentHubDemo.Live;

public interface ILiveCatalogProvider
{
    MeasurementCatalog BuildCatalog();
}

public sealed class LiveCatalogProvider : ILiveCatalogProvider
{
    private readonly ILiveCache _cache;

    public LiveCatalogProvider(ILiveCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public MeasurementCatalog BuildCatalog()
    {
        var keys = _cache.Keys;
        if (keys.Count == 0)
        {
            return MeasurementCatalog.Empty;
        }

        var instruments = new Dictionary<string, InstrumentBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            var (instrumentId, metricName, displayMetric) = ParseKey(key);

            if (!instruments.TryGetValue(instrumentId, out var builder))
            {
                builder = new InstrumentBuilder(instrumentId, BuildInstrumentDisplayName(instrumentId));
                instruments[instrumentId] = builder;
            }

            builder.AddMetric(key, displayMetric, metricName);
        }

        var instrumentSlices = instruments.Values
            .OrderBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(builder => builder.ToSlice())
            .ToList();

        return new MeasurementCatalog
        {
            Instruments = instrumentSlices
        };
    }

    private static (string InstrumentId, string MetricName, string DisplayMetric) ParseKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key) && MeasureKey.TryParse(key, out var parsed))
        {
            var instrument = string.IsNullOrWhiteSpace(parsed.InstrumentId) ? "Ungrouped" : parsed.InstrumentId;
            var metric = string.IsNullOrWhiteSpace(parsed.Metric) ? "Telemetry" : parsed.Metric;
            return (instrument, parsed.Metric, metric);
        }

        var fallbackInstrument = "Ungrouped";
        var fallbackMetric = string.IsNullOrWhiteSpace(key) ? "Telemetry" : key;
        return (fallbackInstrument, fallbackMetric, fallbackMetric);
    }

    private static string BuildInstrumentDisplayName(string instrumentId)
    {
        if (string.IsNullOrWhiteSpace(instrumentId))
        {
            return "Ungrouped";
        }

        var normalized = instrumentId.Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return instrumentId;
        }

        var textInfo = CultureInfo.CurrentCulture.TextInfo;
        var lowered = normalized.ToLowerInvariant();
        var title = textInfo.ToTitleCase(lowered);
        return string.Equals(title, normalized, StringComparison.Ordinal)
            ? normalized
            : title;
    }

    private sealed class InstrumentBuilder
    {
        private readonly List<MetricSlice> _metrics = new();

        public InstrumentBuilder(string id, string displayName)
        {
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public void AddMetric(string key, string displayMetric, string metricName)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var slice = new MetricSlice
            {
                Key = key,
                DisplayName = string.IsNullOrWhiteSpace(displayMetric) ? key : displayMetric,
                Metric = metricName ?? string.Empty,
                IsPreferred = LiveCatalogPreferences.IsPreferredMetric(metricName)
            };

            _metrics.Add(slice);
        }

        public InstrumentSlice ToSlice()
        {
            var ordered = _metrics
                .Where(metric => !string.IsNullOrWhiteSpace(metric.Key))
                .OrderByDescending(metric => metric.IsPreferred)
                .ThenBy(metric => metric.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new InstrumentSlice
            {
                Id = Id,
                DisplayName = DisplayName,
                Metrics = ordered
            };
        }
    }
}

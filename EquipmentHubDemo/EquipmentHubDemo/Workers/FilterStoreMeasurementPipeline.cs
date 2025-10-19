using System;
using System.Collections.Generic;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Predict;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Workers;

public interface IMeasurementPipeline
{
    Task<FilteredMeasurement> ProcessAsync(Measurement measurement, CancellationToken cancellationToken);
}

/// <summary>
/// Applies the filter/store pipeline for incoming measurements. The pipeline mimics the
/// original worker semantics while remaining easy to exercise in unit tests.
/// </summary>
public sealed class FilterStoreMeasurementPipeline : IMeasurementPipeline
{
    private static readonly HashSet<string> DiagnosticMetrics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temperature",
        "Humidity"
    };

    private readonly IMeasurementRepository _measurementRepository;
    private readonly IDiagnosticRepository _diagnosticRepository;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _filterDelay;

    public FilterStoreMeasurementPipeline(
        IMeasurementRepository measurementRepository,
        IDiagnosticRepository diagnosticRepository,
        TimeProvider timeProvider,
        IOptions<FilterStoreOptions> options)
    {
        _measurementRepository = measurementRepository ?? throw new ArgumentNullException(nameof(measurementRepository));
        _diagnosticRepository = diagnosticRepository ?? throw new ArgumentNullException(nameof(diagnosticRepository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(options));
        if (value.FilterDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), value.FilterDelay, "Filter delay must be non-negative.");
        }

        _filterDelay = value.FilterDelay;
    }

    public async Task<FilteredMeasurement> ProcessAsync(Measurement measurement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        if (_filterDelay > TimeSpan.Zero)
        {
            await Task.Delay(_filterDelay, cancellationToken).ConfigureAwait(false);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var filtered = new FilteredMeasurement(measurement.Key, measurement.Value, nowUtc);

        _measurementRepository.AppendHistory(filtered);
        _measurementRepository.UpsertLatest(filtered);

        if (ShouldRecordDiagnostics(filtered.Key.Metric))
        {
            var sample = new DiagnosticSample(
                filtered.Key.InstrumentId,
                filtered.Key.Metric,
                filtered.Value,
                filtered.TimestampUtc);

            await _diagnosticRepository.AddAsync(sample, cancellationToken).ConfigureAwait(false);
        }

        return filtered;
    }

    private static bool ShouldRecordDiagnostics(string metric)
        => DiagnosticMetrics.Contains(metric);
}

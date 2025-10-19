using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Tests;
using EquipmentHubDemo.Workers;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Tests.Workers;

public sealed class FilterStoreMeasurementPipelineTests
{
    private static readonly MeasureKey TemperatureKey = new("IN-01", "Temperature");

    [Fact]
    public async Task ProcessAsync_WritesHistoryAndLatest()
    {
        var measurement = new Measurement(TemperatureKey, 25.4, DateTime.UtcNow);
        var repository = new InMemoryMeasurementRepository();
        var diagnostics = new InMemoryDiagnosticRepository();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2024, 11, 5, 8, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new FilterStoreOptions { FilterDelay = TimeSpan.Zero });
        var pipeline = new FilterStoreMeasurementPipeline(repository, diagnostics, timeProvider, options);

        var filtered = await pipeline.ProcessAsync(measurement, CancellationToken.None);

        Assert.Single(repository.History);
        Assert.Single(repository.Latest);
        Assert.Equal(filtered, repository.History.Single());
        Assert.Equal(filtered, repository.Latest.Single());
        Assert.Single(diagnostics.Samples);
        var sample = diagnostics.Samples.Single();
        Assert.Equal("IN-01", sample.InstrumentId);
        Assert.Equal("Temperature", sample.Metric);
        Assert.Equal(filtered.Value, sample.Value);
        Assert.Equal(filtered.TimestampUtc, sample.TimestampUtc);
    }

    [Fact]
    public async Task ProcessAsync_SkipsNonDiagnosticMetrics()
    {
        var measurement = new Measurement(new MeasureKey("IN-02", "Voltage"), 120.1, DateTime.UtcNow);
        var repository = new InMemoryMeasurementRepository();
        var diagnostics = new InMemoryDiagnosticRepository();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2024, 11, 5, 9, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new FilterStoreOptions { FilterDelay = TimeSpan.Zero });
        var pipeline = new FilterStoreMeasurementPipeline(repository, diagnostics, timeProvider, options);

        var filtered = await pipeline.ProcessAsync(measurement, CancellationToken.None);

        Assert.Single(repository.History);
        Assert.Empty(diagnostics.Samples);
        Assert.Equal(new MeasureKey("IN-02", "Voltage"), filtered.Key);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime, filtered.TimestampUtc);
    }

    private sealed class InMemoryMeasurementRepository : IMeasurementRepository
    {
        public List<FilteredMeasurement> History { get; } = new();
        public List<FilteredMeasurement> Latest { get; } = new();

        public void AppendHistory(FilteredMeasurement f)
            => History.Add(f);

        public void UpsertLatest(FilteredMeasurement f)
        {
            Latest.RemoveAll(existing => existing.Key.Equals(f.Key));
            Latest.Add(f);
        }

        public int DeleteHistoryOlderThan(DateTime cutoffUtc)
            => 0;
    }

    private sealed class InMemoryDiagnosticRepository : IDiagnosticRepository
    {
        public List<DiagnosticSample> Samples { get; } = new();

        public Task AddAsync(DiagnosticSample sample, CancellationToken cancellationToken = default)
        {
            Samples.Add(sample);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiagnosticSample>> GetRecentAsync(string instrumentId, string metric, TimeSpan lookback, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DiagnosticSample>>(Samples);
    }
}

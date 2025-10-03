using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Infrastructure.Predict;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Tests.Predict;

public sealed class PredictiveDiagnosticsServiceTests
{
    [Fact]
    public async Task GetInsightAsync_ComputesStatistics()
    {
        var repository = new TestDiagnosticRepository();
        repository.Seed(
            "IN-400",
            "Temperature",
            new DiagnosticSample("IN-400", "Temperature", 10, new DateTime(2024, 11, 05, 8, 0, 0, DateTimeKind.Utc)),
            new DiagnosticSample("IN-400", "Temperature", 12, new DateTime(2024, 11, 05, 8, 1, 0, DateTimeKind.Utc)),
            new DiagnosticSample("IN-400", "Temperature", 14, new DateTime(2024, 11, 05, 8, 2, 0, DateTimeKind.Utc)));

        var now = new DateTimeOffset(2024, 11, 05, 9, 0, 0, TimeSpan.Zero);
        var options = Options.Create(new PredictiveDiagnosticsOptions { LookbackWindow = TimeSpan.FromHours(1) });
        var service = new PredictiveDiagnosticsService(
            repository,
            options,
            new TestTimeProvider(now),
            NullLogger<PredictiveDiagnosticsService>.Instance);

        var insight = await service.GetInsightAsync("IN-400", "Temperature", CancellationToken.None);

        Assert.Equal("IN-400", insight.InstrumentId);
        Assert.Equal("Temperature", insight.Metric);
        Assert.Equal(now.UtcDateTime, insight.TimestampUtc);
        Assert.Equal(12, insight.Mean, 3);
        Assert.Equal(Math.Sqrt(8.0 / 3.0), insight.StandardDeviation, 3);
        Assert.InRange(insight.FailureProbability, 0.1, 0.2);
    }

    [Fact]
    public async Task GetInsightAsync_WhenNoSamples_ReturnsZeros()
    {
        var repository = new TestDiagnosticRepository();
        var now = new DateTimeOffset(2024, 11, 05, 9, 0, 0, TimeSpan.Zero);
        var options = Options.Create(new PredictiveDiagnosticsOptions { LookbackWindow = TimeSpan.FromHours(1) });
        var service = new PredictiveDiagnosticsService(
            repository,
            options,
            new TestTimeProvider(now),
            NullLogger<PredictiveDiagnosticsService>.Instance);

        var insight = await service.GetInsightAsync("IN-401", "Humidity", CancellationToken.None);

        Assert.Equal(0, insight.Mean);
        Assert.Equal(0, insight.StandardDeviation);
        Assert.Equal(0, insight.FailureProbability);
    }
}

internal sealed class TestDiagnosticRepository : IDiagnosticRepository
{
    private readonly Dictionary<(string InstrumentId, string Metric), List<DiagnosticSample>> _data = new();
    public List<DiagnosticSample> Added { get; } = new();

    public void Seed(string instrumentId, string metric, params DiagnosticSample[] samples)
        => _data[(instrumentId, metric)] = samples.ToList();

    public Task AddAsync(DiagnosticSample sample, CancellationToken cancellationToken = default)
    {
        Added.Add(sample);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DiagnosticSample>> GetRecentAsync(string instrumentId, string metric, TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        _data.TryGetValue((instrumentId, metric), out var samples);
        return Task.FromResult<IReadOnlyList<DiagnosticSample>>(samples?.ToList() ?? new List<DiagnosticSample>());
    }
}

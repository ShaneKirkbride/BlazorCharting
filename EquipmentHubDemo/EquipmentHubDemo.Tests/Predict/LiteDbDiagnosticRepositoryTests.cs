using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Infrastructure.Predict;
using Xunit;

namespace EquipmentHubDemo.Tests.Predict;

public sealed class LiteDbDiagnosticRepositoryTests : IDisposable
{
    private readonly string _rootPath;

    public LiteDbDiagnosticRepositoryTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "ehd-diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public async Task AddAndQueryDiagnostics_RoundTripsSamples()
    {
        var dbPath = Path.Combine(_rootPath, "diagnostics", "samples.db");
        using var repository = new LiteDbDiagnosticRepository(dbPath);

        var timestamp = DateTime.UtcNow;
        await repository.AddAsync(new DiagnosticSample("IN-01", "Temperature", 25.4, timestamp));
        await repository.AddAsync(new DiagnosticSample("IN-01", "Temperature", 26.0, timestamp.AddMinutes(5)));
        await repository.AddAsync(new DiagnosticSample("IN-01", "Humidity", 41.2, timestamp.AddMinutes(10)));

        var lookback = TimeSpan.FromMinutes(15);
        var samples = await repository.GetRecentAsync("IN-01", "Temperature", lookback, CancellationToken.None);

        Assert.Equal(2, samples.Count);
        Assert.Equal("IN-01", samples[0].InstrumentId);
        Assert.Equal("Temperature", samples[0].Metric);
        Assert.Equal(25.4, samples[0].Value, 3);
        Assert.Equal(timestamp, samples[0].TimestampUtc, TimeSpan.FromSeconds(1));

        Assert.Equal("IN-01", samples[1].InstrumentId);
        Assert.Equal("Temperature", samples[1].Metric);
        Assert.Equal(26.0, samples[1].Value, 3);
        Assert.Equal(timestamp.AddMinutes(5), samples[1].TimestampUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetRecentAsync_FiltersByLookbackWindow()
    {
        var dbPath = Path.Combine(_rootPath, "diagnostics", "filtering.db");
        using var repository = new LiteDbDiagnosticRepository(dbPath);

        var now = DateTime.UtcNow;
        await repository.AddAsync(new DiagnosticSample("IN-02", "Temperature", 22.1, now.AddHours(-3)));
        await repository.AddAsync(new DiagnosticSample("IN-02", "Temperature", 23.0, now.AddMinutes(-10)));

        var samples = await repository.GetRecentAsync("IN-02", "Temperature", TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Single(samples);
        Assert.Equal(23.0, samples[0].Value, 3);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
            // Swallow IO cleanup exceptions so they don't hide test failures.
        }
    }
}

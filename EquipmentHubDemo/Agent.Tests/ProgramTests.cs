using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text.Json;
using EquipmentHubDemo.Domain.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agent.Tests;

public static class ProgramTests
{
    [Fact]
    public static void MeasureKey_ToString_ReturnsInstrumentAndMetric()
    {
        var key = new MeasureKey("UXG-99", "Noise");

        var result = key.ToString();

        Assert.Equal("UXG-99:Noise", result);
    }

    [Fact]
    public static void Measurement_WithExpression_CreatesUpdatedCopy()
    {
        var timestamp = new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        var key = new MeasureKey("UXG-42", "Power (240VAC)");
        var measurement = new Measurement(key, 10.5, timestamp);

        var updated = measurement with { Value = 12.3 };

        Assert.Equal(key, updated.Key);
        Assert.Equal(timestamp, updated.TimestampUtc);
        Assert.Equal(12.3, updated.Value);
        Assert.NotSame(measurement, updated);
    }

    [Fact]
    public static void Measurement_SerializesWithCamelCasePropertyNames()
    {
        var timestamp = new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc);
        var measurement = new Measurement(
            new MeasureKey("UXG-11", "Humidity"),
            42.1,
            timestamp);

        using var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(measurement, MeasurementJson.Options));
        var root = jsonDoc.RootElement;

        Assert.True(root.TryGetProperty("key", out var keyElement));
        Assert.Equal("UXG-11", keyElement.GetProperty("instrumentId").GetString());
        Assert.Equal("Humidity", keyElement.GetProperty("metric").GetString());
        Assert.Equal(42.1, root.GetProperty("value").GetDouble());
        Assert.Equal(timestamp, root.GetProperty("timestampUtc").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public static void AgentOptions_Normalize_EnforcesPublisherGuards()
    {
        var defaults = new AgentOptions();
        defaults.Normalize();

        var options = new AgentOptions
        {
            SendTimeoutMilliseconds = 0,
            SendHighWatermark = -5
        };

        options.Normalize();

        Assert.Equal(defaults.SendTimeoutMilliseconds, options.SendTimeoutMilliseconds);
        Assert.Equal(defaults.SendHighWatermark, options.SendHighWatermark);
        Assert.Equal(defaults.SendTimeout, options.SendTimeout);
    }

    [Fact]
    public static void MeasurementGenerator_RespectsConfiguredInstrumentsAndMetrics()
    {
        var options = Options.Create(new AgentOptions
        {
            Instruments = new List<InstrumentOptions>
            {
                new()
                {
                    InstrumentId = "A-01",
                    Metrics = new List<string> { "Power (240VAC)" }
                },
                new()
                {
                    InstrumentId = "B-02",
                    Metrics = new List<string> { "Humidity", "Custom" }
                }
            }
        });
        options.Value.Normalize();

        var monitorOptions = Options.Create(new InstrumentMonitorOptions
        {
            Instruments = new[] { "A-01", "B-02" }
        });

        var timestamp = new DateTime(2024, 07, 08, 09, 10, 11, DateTimeKind.Utc);
        var generator = new MeasurementGenerator(
            options,
            monitorOptions,
            new StubScpiClient(),
            new StaticTimeProvider(timestamp),
            new Random(0),
            NullLogger<MeasurementGenerator>.Instance);

        var results = generator.CreateMeasurements(timestamp).ToList();

        Assert.Equal(10, results.Count);
        Assert.Contains(results, m => m.Key.InstrumentId == "A-01" && m.Key.Metric == "Power (240VAC)"
            && m.Value != default);
        Assert.Contains(results, m => m.Key.InstrumentId == "B-02" && m.Key.Metric == "Humidity" && Math.Abs(m.Value - 40.0) < 0.001);
        Assert.Contains(results, m => m.Key.InstrumentId == "B-02" && m.Key.Metric == "Custom");
        Assert.True(results.Count(m => m.Key.Metric == "Heartbeat") == 2);
        Assert.True(results.Count(m => m.Key.Metric == "SelfCheck") == 2);
        Assert.True(results.Count(m => m.Key.Metric == "Temperature") == 2);
    }
}

internal sealed class StubScpiClient : IScpiCommandClient
{
    public Task<string> SendAsync(string instrumentId, string command, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(command switch
        {
            "*IDN?" => $"StubCo,Model,{instrumentId},1.0",
            "SELF:CHECK?" => "0,OK",
            "MEAS:TEMP?" => "25.5",
            "MEAS:HUM?" => "40.0",
            _ => throw new InvalidOperationException($"Unexpected command {command}.")
        });
    }
}

internal sealed class StaticTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public StaticTimeProvider(DateTime utcNow)
        => _utcNow = new DateTimeOffset(utcNow, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new NoopTimer();

    private sealed class NoopTimer : ITimer
    {
        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
    }
}

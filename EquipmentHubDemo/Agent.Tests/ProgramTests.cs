using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        var key = new MeasureKey("UXG-42", "Power");
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
            new MeasureKey("UXG-11", "SNR"),
            42.1,
            timestamp);

        using var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(measurement, MeasurementJson.Options));
        var root = jsonDoc.RootElement;

        Assert.True(root.TryGetProperty("key", out var keyElement));
        Assert.Equal("UXG-11", keyElement.GetProperty("instrumentId").GetString());
        Assert.Equal("SNR", keyElement.GetProperty("metric").GetString());
        Assert.Equal(42.1, root.GetProperty("value").GetDouble());
        Assert.Equal(timestamp, root.GetProperty("timestampUtc").GetDateTime().ToUniversalTime());
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
                    Metrics = new List<string> { "Power" }
                },
                new()
                {
                    InstrumentId = "B-02",
                    Metrics = new List<string> { "SNR", "Custom" }
                }
            }
        });
        options.Value.Normalize();

        var generator = new MeasurementGenerator(options, new Random(0), NullLogger<MeasurementGenerator>.Instance);
        var timestamp = new DateTime(2024, 07, 08, 09, 10, 11, DateTimeKind.Utc);

        var results = generator.CreateMeasurements(timestamp).ToList();

        Assert.Equal(3, results.Count);
        Assert.Contains(results, m => m.Key.InstrumentId == "A-01" && m.Key.Metric == "Power");
        Assert.Contains(results, m => m.Key.InstrumentId == "B-02" && m.Key.Metric == "SNR");
        Assert.Contains(results, m => m.Key.InstrumentId == "B-02" && m.Key.Metric == "Custom");
    }
}

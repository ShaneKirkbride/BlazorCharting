using System.Text.Json;

namespace Agent;

public readonly record struct MeasureKey(string InstrumentId, string Metric)
{
    public override string ToString() => $"{InstrumentId}:{Metric}";
}

public sealed record Measurement(MeasureKey Key, double Value, DateTime TimestampUtc);

internal static class MeasurementJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

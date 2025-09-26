namespace EquipmentHubDemo.Domain;

public readonly record struct MeasureKey(string InstrumentId, string Metric)
{
    public override string ToString() => $"{InstrumentId}:{Metric}";
    public static bool TryParse(string s, out MeasureKey key)
    {
        key = default;
        var parts = s.Split(':', 2);
        if (parts.Length != 2) return false;
        key = new(parts[0], parts[1]);
        return true;
    }
}

public sealed record Measurement(MeasureKey Key, double Value, DateTime TimestampUtc);
public sealed record FilteredMeasurement(MeasureKey Key, double Value, DateTime TimestampUtc);

public interface IMeasurementRepository
{
    void AppendHistory(FilteredMeasurement f);
    void UpsertLatest(FilteredMeasurement f);
    int DeleteHistoryOlderThan(DateTime cutoffUtc);
}

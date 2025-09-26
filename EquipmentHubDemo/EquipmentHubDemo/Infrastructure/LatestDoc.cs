using LiteDB;

namespace EquipmentHubDemo.Infrastructure;

internal sealed class LatestDoc
{
    [BsonId] public string Id { get; set; } = default!; // InstrumentId:Metric
    public string InstrumentId { get; set; } = default!;
    public string Metric { get; set; } = default!;
    public double Value { get; set; }
    public DateTime TimestampUtc { get; set; }
}

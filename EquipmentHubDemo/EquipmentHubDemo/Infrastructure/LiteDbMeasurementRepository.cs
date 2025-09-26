using EquipmentHubDemo.Domain;
using LiteDB;

namespace EquipmentHubDemo.Infrastructure;

public sealed class LiteDbMeasurementRepository : IMeasurementRepository, IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<FilteredMeasurement> _history;
    private readonly ILiteCollection<LatestDoc> _latest;

    public LiteDbMeasurementRepository(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _db = new LiteDatabase(path);
        _history = _db.GetCollection<FilteredMeasurement>("history");
        _latest = _db.GetCollection<LatestDoc>("latest");

        // History indexes for queries
        _history.EnsureIndex(x => x.Key.InstrumentId);
        _history.EnsureIndex(x => x.Key.Metric);
        _history.EnsureIndex(x => x.TimestampUtc);

        // Latest uses Id (InstrumentId:Metric) for uniqueness; simple query indexes are fine
        _latest.EnsureIndex(x => x.InstrumentId);
        _latest.EnsureIndex(x => x.Metric);

        // IMPORTANT: remove any previous EnsureIndex with unique composite on _latest
        // e.g., remove: _latest.EnsureIndex("key_inst_metric", x => new { x.Key.InstrumentId, x.Key.Metric }, unique: true);
    }

    public void AppendHistory(FilteredMeasurement f) => _history.Insert(f);

    public void UpsertLatest(FilteredMeasurement f)
    {
        var doc = new LatestDoc
        {
            Id = $"{f.Key.InstrumentId}:{f.Key.Metric}",
            InstrumentId = f.Key.InstrumentId,
            Metric = f.Key.Metric,
            Value = f.Value,
            TimestampUtc = f.TimestampUtc
        };
        _latest.Upsert(doc); // idempotent for same InstrumentId:Metric
    }

    public int DeleteHistoryOlderThan(DateTime cutoffUtc)
        => _history.DeleteMany(h => h.TimestampUtc < cutoffUtc);

    public void Dispose() => _db.Dispose();
}

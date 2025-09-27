using System.Linq;
using EquipmentHubDemo.Domain.Predict;
using LiteDB;

namespace EquipmentHubDemo.Infrastructure.Predict;

public sealed class LiteDbDiagnosticRepository : IDiagnosticRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<DiagnosticSample> _collection;

    public LiteDbDiagnosticRepository(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _database = new LiteDatabase(path);
        _collection = _database.GetCollection<DiagnosticSample>("diagnostics");
        _collection.EnsureIndex(x => x.InstrumentId);
        _collection.EnsureIndex(x => x.Metric);
        _collection.EnsureIndex(x => x.TimestampUtc);
    }

    public Task AddAsync(DiagnosticSample sample, CancellationToken cancellationToken = default)
    {
        _collection.Insert(sample);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DiagnosticSample>> GetRecentAsync(string instrumentId, string metric, TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);

        var cutoff = DateTime.UtcNow - lookback;
        var results = _collection
            .Find(s => s.InstrumentId == instrumentId && s.Metric == metric && s.TimestampUtc >= cutoff)
            .OrderBy(s => s.TimestampUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<DiagnosticSample>>(results);
    }

    public void Dispose() => _database.Dispose();
}

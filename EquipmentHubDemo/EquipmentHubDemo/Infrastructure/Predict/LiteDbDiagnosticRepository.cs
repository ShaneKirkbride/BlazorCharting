using System.Linq;
using EquipmentHubDemo.Domain.Predict;
using LiteDB;

namespace EquipmentHubDemo.Infrastructure.Predict;

public sealed class LiteDbDiagnosticRepository : IDiagnosticRepository, IDisposable
{
    private sealed class DiagnosticSampleDocument
    {
        [BsonId]
        public ObjectId Id { get; set; } = ObjectId.NewObjectId();
        public string InstrumentId { get; set; } = default!;
        public string Metric { get; set; } = default!;
        public double Value { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    private readonly LiteDatabase _database;
    private readonly ILiteCollection<DiagnosticSampleDocument> _collection;

    public LiteDbDiagnosticRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new ConnectionString
        {
            Filename = path,
            Connection = ConnectionType.Shared
        };

        _database = new LiteDatabase(connection);
        _collection = _database.GetCollection<DiagnosticSampleDocument>("diagnostics");
        _collection.EnsureIndex(x => x.InstrumentId);
        _collection.EnsureIndex(x => x.Metric);
        _collection.EnsureIndex(x => x.TimestampUtc);
    }

    public Task AddAsync(DiagnosticSample sample, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var doc = new DiagnosticSampleDocument
        {
            InstrumentId = sample.InstrumentId,
            Metric = sample.Metric,
            Value = sample.Value,
            TimestampUtc = EnsureUtc(sample.TimestampUtc)
        };

        _collection.Insert(doc);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DiagnosticSample>> GetRecentAsync(string instrumentId, string metric, TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);

        if (lookback <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback), lookback, "Lookback window must be positive.");
        }

        var cutoffUtc = DateTime.UtcNow - lookback;

        var documents = _collection
            .Query()
            .Where(doc => doc.InstrumentId == instrumentId && doc.Metric == metric && doc.TimestampUtc >= cutoffUtc)
            .OrderBy(doc => doc.TimestampUtc)
            .ToList();

        var results = documents
            .Select(ToDomain)
            .ToList();

        return Task.FromResult<IReadOnlyList<DiagnosticSample>>(results);
    }

    public void Dispose() => _database.Dispose();

    private static DiagnosticSample ToDomain(DiagnosticSampleDocument document)
    {
        var timestampUtc = EnsureUtc(document.TimestampUtc);
        return new DiagnosticSample(document.InstrumentId, document.Metric, document.Value, timestampUtc);
    }

    private static DateTime EnsureUtc(DateTime timestamp)
        => timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
            _ => timestamp.ToUniversalTime()
        };
}

using System.Linq;
using System.Threading;
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

    private readonly object _sync = new();
    private readonly string _path;
    private readonly ConnectionString _connectionString;
    private LiteDatabase _database = null!;
    private ILiteCollection<DiagnosticSampleDocument> _collection = null!;

    public LiteDbDiagnosticRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _path = path;
        _connectionString = new ConnectionString
        {
            Filename = path,
            Connection = ConnectionType.Shared
        };

        InitializeDatabase();
    }

    public Task AddAsync(DiagnosticSample sample, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);

        cancellationToken.ThrowIfCancellationRequested();

        Execute(collection =>
        {
            var doc = new DiagnosticSampleDocument
            {
                InstrumentId = sample.InstrumentId,
                Metric = sample.Metric,
                Value = sample.Value,
                TimestampUtc = EnsureUtc(sample.TimestampUtc)
            };

            collection.Insert(doc);
            return true;
        });

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

        cancellationToken.ThrowIfCancellationRequested();

        var results = Execute(collection =>
        {
            var documents = collection
                .Query()
                .Where(doc => doc.InstrumentId == instrumentId && doc.Metric == metric && doc.TimestampUtc >= cutoffUtc)
                .OrderBy(doc => doc.TimestampUtc)
                .ToList();

            return documents
                .Select(ToDomain)
                .ToList();
        });

        return Task.FromResult<IReadOnlyList<DiagnosticSample>>(results);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _collection = null!;
            _database?.Dispose();
            _database = null!;
        }

        GC.SuppressFinalize(this);
    }

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

    private void InitializeDatabase()
    {
        lock (_sync)
        {
            ResetCollections(CreateDatabase());
        }
    }

    private LiteDatabase CreateDatabase()
    {
        try
        {
            return new LiteDatabase(_connectionString);
        }
        catch (LiteException ex) when (IsCorruptedDatabase(ex))
        {
            RecreateDatabaseFile();
            return new LiteDatabase(_connectionString);
        }
    }

    private void ResetCollections(LiteDatabase database)
    {
        _database?.Dispose();
        _database = database;
        _collection = _database.GetCollection<DiagnosticSampleDocument>("diagnostics");
        _collection.EnsureIndex(x => x.InstrumentId);
        _collection.EnsureIndex(x => x.Metric);
        _collection.EnsureIndex(x => x.TimestampUtc);
    }

    private void RecreateDatabaseFile()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private T Execute<T>(Func<ILiteCollection<DiagnosticSampleDocument>, T> action)
    {
        lock (_sync)
        {
            try
            {
                return action(_collection);
            }
            catch (LiteException ex) when (IsCorruptedDatabase(ex))
            {
                ResetCollections(CreateDatabase());
                return action(_collection);
            }
        }
    }

    private static bool IsCorruptedDatabase(LiteException exception)
        => exception.Message.Contains("Page type must be data page", StringComparison.Ordinal);
}

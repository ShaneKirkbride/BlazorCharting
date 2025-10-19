using System;
using EquipmentHubDemo.Domain;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Workers;

public interface ITtlCleanupService
{
    TtlCleanupResult Cleanup(DateTime currentUtc);
}

public readonly record struct TtlCleanupResult(DateTime CutoffUtc, int DeletedCount);

/// <summary>
/// Performs the measurement history cleanup used by <see cref="TtlWorker"/>.
/// </summary>
public sealed class TtlCleanupService : ITtlCleanupService
{
    private readonly IMeasurementRepository _repository;
    private readonly TimeSpan _historyRetention;

    public TtlCleanupService(IMeasurementRepository repository, IOptions<TtlWorkerOptions> options)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(options));
        if (value.HistoryRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), value.HistoryRetention, "HistoryRetention must be positive.");
        }

        _historyRetention = value.HistoryRetention;
    }

    public TtlCleanupResult Cleanup(DateTime currentUtc)
    {
        var cutoffUtc = currentUtc - _historyRetention;
        var deleted = _repository.DeleteHistoryOlderThan(cutoffUtc);
        return new TtlCleanupResult(cutoffUtc, deleted);
    }
}

using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Workers;

/// <summary>Deletes old history rows every minute.</summary>
public sealed class TtlWorker : BackgroundService
{
    private readonly IMeasurementRepository _repo;
    private readonly TimeSpan _historyRetention;

    public TtlWorker(IMeasurementRepository repo, IOptions<TtlWorkerOptions> options)
    {
        _repo = repo;
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(options));

        if (value.HistoryRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), value.HistoryRetention, "HistoryRetention must be positive.");
        }

        _historyRetention = value.HistoryRetention;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var cut = DateTime.UtcNow - _historyRetention;
            _repo.DeleteHistoryOlderThan(cut);
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
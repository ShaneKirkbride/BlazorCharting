using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace EquipmentHubDemo.Workers;

/// <summary>Deletes old history rows every minute.</summary>
public sealed class TtlWorker : BackgroundService
{
    private readonly IMeasurementRepository _repo;
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(30); // keep last 30 minutes

    public TtlWorker(IMeasurementRepository repo) => _repo = repo;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var cut = DateTime.UtcNow - _ttl;
            _repo.DeleteHistoryOlderThan(cut);
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
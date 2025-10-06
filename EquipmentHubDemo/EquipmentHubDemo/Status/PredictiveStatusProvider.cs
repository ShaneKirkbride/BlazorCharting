using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Live;

namespace EquipmentHubDemo.Status;

public sealed class PredictiveStatusProvider
{
    private static readonly HashSet<string> PredictiveMetrics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temperature",
        "Humidity"
    };

    private readonly ILiveCache _liveCache;
    private readonly IPredictiveMaintenanceService _maintenanceService;

    public PredictiveStatusProvider(
        ILiveCache liveCache,
        IPredictiveMaintenanceService maintenanceService)
    {
        _liveCache = liveCache ?? throw new ArgumentNullException(nameof(liveCache));
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
    }

    public async Task<IReadOnlyList<PredictiveStatus>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        var keys = _liveCache.Keys;
        if (keys.Count == 0)
        {
            return Array.Empty<PredictiveStatus>();
        }

        var tasks = new List<Task<PredictiveStatus?>>();
        foreach (var key in keys)
        {
            if (!MeasureKey.TryParse(key, out var parsed))
            {
                continue;
            }

            if (!PredictiveMetrics.Contains(parsed.Metric))
            {
                continue;
            }

            tasks.Add(BuildStatusAsync(parsed, cancellationToken));
        }

        if (tasks.Count == 0)
        {
            return Array.Empty<PredictiveStatus>();
        }

        var resolved = await Task.WhenAll(tasks).ConfigureAwait(false);
        return resolved
            .Where(status => status is not null)
            .Select(status => status!)
            .OrderBy(status => status.InstrumentId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Metric, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<PredictiveStatus?> BuildStatusAsync(MeasureKey key, CancellationToken cancellationToken)
    {
        var summary = await _maintenanceService
            .GetSummaryAsync(key.InstrumentId, key.Metric, cancellationToken)
            .ConfigureAwait(false);

        return new PredictiveStatus(
            key.InstrumentId,
            key.Metric,
            summary.Insight,
            summary.ServicePlan,
            summary.RepairPlan);
    }
}

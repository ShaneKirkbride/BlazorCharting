using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Domain.Predict;

namespace EquipmentHubDemo.Domain.Status;

public interface ISystemStatusClient
{
    Task<IReadOnlyList<PredictiveStatus>> GetPredictiveStatusesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringStatus>> GetMonitoringStatusesAsync(CancellationToken cancellationToken = default);
}

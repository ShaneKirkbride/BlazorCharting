using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Domain.Status;

namespace EquipmentHubDemo.Client.Services;

public sealed class HttpSystemStatusClient : ISystemStatusClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpJsonProbe _probe;

    public HttpSystemStatusClient(HttpClient httpClient, IApiBaseUriProvider baseUriProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUriProvider);

        _probe = new HttpJsonProbe(
            httpClient,
            baseUriProvider,
            SerializerOptions,
            static (_, _, aggregateMessage) => new SystemStatusClientException(aggregateMessage));
    }

    public Task<IReadOnlyList<PredictiveStatus>> GetPredictiveStatusesAsync(CancellationToken cancellationToken = default)
        => _probe.FetchListAsync<PredictiveStatus>("api/predictive/status", cancellationToken);

    public Task<IReadOnlyList<MonitoringStatus>> GetMonitoringStatusesAsync(CancellationToken cancellationToken = default)
        => _probe.FetchListAsync<MonitoringStatus>("api/monitoring/status", cancellationToken);
}

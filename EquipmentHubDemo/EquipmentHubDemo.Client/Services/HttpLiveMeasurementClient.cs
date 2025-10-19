using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Live;

namespace EquipmentHubDemo.Client.Services;

/// <summary>
/// HTTP-based implementation of <see cref="ILiveMeasurementClient"/> that can probe multiple candidate endpoints.
/// </summary>
public sealed class HttpLiveMeasurementClient : ILiveMeasurementClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpJsonProbe _probe;

    public HttpLiveMeasurementClient(
        HttpClient httpClient,
        IApiBaseUriProvider baseUriProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUriProvider);

        _probe = new HttpJsonProbe(
            httpClient,
            baseUriProvider,
            SerializerOptions,
            static (_, _, aggregateMessage) => new LiveMeasurementClientException(aggregateMessage));
    }

    public async Task<MeasurementCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _probe.FetchAsync<MeasurementCatalog>("api/live/catalog", cancellationToken).ConfigureAwait(false);
        return catalog ?? MeasurementCatalog.Empty;
    }

    public async Task<IReadOnlyList<string>> GetAvailableKeysAsync(CancellationToken cancellationToken = default)
    {
        var result = await _probe.FetchListAsync<string>("api/keys", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<PointDto>> GetMeasurementsAsync(string key, long sinceTicks, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        if (sinceTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sinceTicks), sinceTicks, "sinceTicks must be non-negative.");
        }

        var relativePath = $"api/live?key={Uri.EscapeDataString(key)}&sinceTicks={sinceTicks}";
        return await _probe.FetchListAsync<PointDto>(relativePath, cancellationToken).ConfigureAwait(false);
    }
}

using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
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

    private readonly HttpClient _httpClient;
    private readonly IApiBaseUriProvider _baseUriProvider;
    private Uri? _preferredBaseUri;

    public HttpLiveMeasurementClient(
        HttpClient httpClient,
        IApiBaseUriProvider baseUriProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUriProvider = baseUriProvider ?? throw new ArgumentNullException(nameof(baseUriProvider));
    }

    public async Task<IReadOnlyList<string>> GetAvailableKeysAsync(CancellationToken cancellationToken = default)
    {
        var result = await FetchListAsync<string>("api/keys", cancellationToken).ConfigureAwait(false);
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
        return await FetchListAsync<PointDto>(relativePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TElement>> FetchListAsync<TElement>(string relativePath, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var baseUri in EnumerateBaseUris())
        {
            var requestUri = new Uri(baseUri, relativePath);

            try
            {
                using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"[{requestUri}] HTTP {(int)response.StatusCode} ({response.ReasonPhrase})");
                    continue;
                }

                if (!IsJsonPayload(response.Content.Headers.ContentType, payload))
                {
                    var mediaType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                    errors.Add($"[{requestUri}] Non-JSON payload (Content-Type '{mediaType}', Body '{Preview(payload)}')");
                    continue;
                }

                try
                {
                    var deserialized = JsonSerializer.Deserialize<List<TElement>>(payload, SerializerOptions) ?? new List<TElement>();
                    Volatile.Write(ref _preferredBaseUri, baseUri);
                    return deserialized;
                }
                catch (JsonException ex)
                {
                    errors.Add($"[{requestUri}] JSON parse error: {ex.Message}");
                }
            }
            catch (HttpRequestException ex)
            {
                errors.Add($"[{requestUri}] {ex.Message}");
            }
        }

        throw new LiveMeasurementClientException(BuildAggregateError(relativePath, errors));
    }

    private IEnumerable<Uri> EnumerateBaseUris()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preferred = Volatile.Read(ref _preferredBaseUri);
        if (preferred is not null && visited.Add(preferred.AbsoluteUri))
        {
            yield return preferred;
        }

        foreach (var candidate in _baseUriProvider.GetBaseUris())
        {
            if (visited.Add(candidate.AbsoluteUri))
            {
                yield return candidate;
            }
        }
    }

    private static bool IsJsonPayload(MediaTypeHeaderValue? contentType, string payload)
    {
        if (contentType is not null && !string.IsNullOrWhiteSpace(contentType.MediaType))
        {
            return contentType.MediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return true;
        }

        var trimmed = payload.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
    }

    private static string Preview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        const int maxLength = 120;
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength] + "…";
    }

    private static string BuildAggregateError(string relativePath, IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
        {
            return $"No API endpoints were reachable for '{relativePath}'.";
        }

        var builder = new StringBuilder();
        builder.Append($"Unable to retrieve '{relativePath}'. Attempted endpoints: ");
        builder.Append(string.Join("; ", errors));
        return builder.ToString();
    }

}

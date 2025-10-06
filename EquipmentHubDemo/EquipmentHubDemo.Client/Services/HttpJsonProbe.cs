using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace EquipmentHubDemo.Client.Services;

internal sealed class HttpJsonProbe
{
    private readonly HttpClient _httpClient;
    private readonly IApiBaseUriProvider _baseUriProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Func<string, IReadOnlyList<string>, string, Exception> _exceptionFactory;
    private Uri? _preferredBaseUri;

    public HttpJsonProbe(
        HttpClient httpClient,
        IApiBaseUriProvider baseUriProvider,
        JsonSerializerOptions serializerOptions,
        Func<string, IReadOnlyList<string>, string, Exception> exceptionFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUriProvider = baseUriProvider ?? throw new ArgumentNullException(nameof(baseUriProvider));
        _serializerOptions = serializerOptions ?? throw new ArgumentNullException(nameof(serializerOptions));
        _exceptionFactory = exceptionFactory ?? throw new ArgumentNullException(nameof(exceptionFactory));
    }

    public async Task<IReadOnlyList<TElement>> FetchListAsync<TElement>(string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        var errors = new List<string>();

        foreach (var baseUri in EnumerateBaseUris())
        {
            var requestUri = new Uri(baseUri, relativePath);

            try
            {
                using var response = await _httpClient
                    .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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
                    var deserialized = JsonSerializer.Deserialize<List<TElement>>(payload, _serializerOptions) ?? new List<TElement>();
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

        var aggregateMessage = BuildAggregateError(relativePath, errors);
        throw _exceptionFactory(relativePath, errors, aggregateMessage);
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

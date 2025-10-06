using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Client.Services;

/// <summary>
/// Default implementation that resolves API base URIs from configuration and runtime heuristics.
/// </summary>
public sealed class ApiBaseUriProvider : IApiBaseUriProvider
{
    private readonly IReadOnlyList<Uri> _baseUris;

    public ApiBaseUriProvider(
        IOptions<ApiClientOptions> options,
        NavigationManager navigationManager)
    {
        ArgumentNullException.ThrowIfNull(navigationManager);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<Uri>();

        void TryAdd(Uri? candidate)
        {
            if (candidate is null)
            {
                return;
            }

            var normalized = EnsureTrailingSlash(candidate.AbsoluteUri);
            if (seen.Add(normalized))
            {
                ordered.Add(new Uri(normalized, UriKind.Absolute));
            }
        }

        var optionsValue = options?.Value;
        var configured = optionsValue?.BaseAddresses ?? Array.Empty<string>();
        foreach (var address in configured)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                TryAdd(uri);
            }
        }

        if (Uri.TryCreate(navigationManager.BaseUri, UriKind.Absolute, out var navBase))
        {
            TryAdd(navBase);

            var enableLoopbackFallbacks = optionsValue?.EnableLoopbackFallbacks ?? true;
            if (navBase.IsLoopback && enableLoopbackFallbacks)
            {
                var loopbackCandidates = optionsValue?.LoopbackFallbackAddresses ?? Array.Empty<string>();
                foreach (var candidate in loopbackCandidates)
                {
                    if (Uri.TryCreate(candidate, UriKind.Absolute, out var loopback))
                    {
                        TryAdd(loopback);
                    }
                }
            }
        }

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException(
                "No valid API base addresses could be resolved. Configure ApiClient:BaseAddresses or provide a loopback fallback.");
        }

        _baseUris = ordered;
    }

    public IReadOnlyList<Uri> GetBaseUris() => _baseUris;

    private static string EnsureTrailingSlash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }
}

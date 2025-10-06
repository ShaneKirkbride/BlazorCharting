using System.Collections.Generic;

namespace EquipmentHubDemo.Client.Services;

/// <summary>
/// Configuration options for resolving the EquipmentHub server API endpoint.
/// </summary>
public sealed class ApiClientOptions
{
    public const string SectionName = "ApiClient";

    /// <summary>
    /// Optional list of base addresses (absolute URLs) that will be probed in order when contacting the server.
    /// </summary>
    public IList<string> BaseAddresses { get; init; } = new List<string>();

    /// <summary>
    /// Candidate loopback endpoints that can be used when the application is running on a developer machine and
    /// no explicit base addresses were configured. This allows the WASM client (served from a random loopback port)
    /// to seamlessly reach the ASP.NET Core server that typically listens on deterministic ports.
    /// </summary>
    public IList<string> LoopbackFallbackAddresses { get; init; } = new List<string>
    {
        "https://localhost:7118/",
        "http://localhost:5026/"
    };

    /// <summary>
    /// Indicates whether <see cref="LoopbackFallbackAddresses"/> should be probed when the application is hosted on a
    /// loopback address. Disabling this avoids cross-origin probes when the client is served standalone without the
    /// companion ASP.NET Core host.
    /// </summary>
    public bool EnableLoopbackFallbacks { get; init; } = true;
}

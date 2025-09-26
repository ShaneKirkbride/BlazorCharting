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
}

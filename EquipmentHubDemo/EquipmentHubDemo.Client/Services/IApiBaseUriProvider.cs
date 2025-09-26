using System;
using System.Collections.Generic;

namespace EquipmentHubDemo.Client.Services;

/// <summary>
/// Provides the ordered list of base URIs that the client should probe when contacting the server API.
/// </summary>
public interface IApiBaseUriProvider
{
    /// <summary>
    /// Resolves the candidate base URIs in priority order. Returned URIs are guaranteed to be absolute and unique.
    /// </summary>
    IReadOnlyList<Uri> GetBaseUris();
}

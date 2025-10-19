using EquipmentHubDemo.Domain;

namespace EquipmentHubDemo.Domain.Live;

/// <summary>
/// Defines the contract for retrieving live measurement metadata and data points.
/// </summary>
public interface ILiveMeasurementClient
{
    /// <summary>
    /// Retrieves the live measurement catalog grouped by instrument.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A catalog describing the available instruments and metrics.</returns>
    Task<MeasurementCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the list of available measurement keys that can be charted.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A read-only list of measurement keys.</returns>
    Task<IReadOnlyList<string>> GetAvailableKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the live data points for a given measurement key.
    /// </summary>
    /// <param name="key">The measurement identifier.</param>
    /// <param name="sinceTicks">Only points with a timestamp greater than this tick count will be returned.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A read-only list of measurement points ordered by timestamp.</returns>
    Task<IReadOnlyList<PointDto>> GetMeasurementsAsync(string key, long sinceTicks, CancellationToken cancellationToken = default);
}

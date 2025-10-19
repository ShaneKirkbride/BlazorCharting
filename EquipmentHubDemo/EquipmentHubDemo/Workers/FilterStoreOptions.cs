namespace EquipmentHubDemo.Workers;

/// <summary>
/// Configuration options for the filter/store measurement pipeline.
/// </summary>
public sealed record FilterStoreOptions
{
    public const string SectionName = "FilterStore";

    /// <summary>
    /// Gets the amount of time to wait before storing a measurement. This simulates
    /// downstream processing latency while keeping tests deterministic.
    /// </summary>
    public TimeSpan FilterDelay { get; init; } = TimeSpan.FromMilliseconds(200);
}

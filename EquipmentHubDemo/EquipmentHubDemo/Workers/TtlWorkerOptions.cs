namespace EquipmentHubDemo.Workers;

public sealed record TtlWorkerOptions
{
    public const string SectionName = "TtlWorker";

    /// <summary>
    ///     Duration to retain historical measurements before pruning.
    /// </summary>
    public TimeSpan HistoryRetention { get; init; } = TimeSpan.FromMinutes(30);
}

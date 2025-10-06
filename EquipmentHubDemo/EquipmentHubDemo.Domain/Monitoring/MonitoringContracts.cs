namespace EquipmentHubDemo.Domain.Monitoring;

/// <summary>
/// Exception thrown when a monitoring command cannot be executed successfully and escalation is required.
/// </summary>
public sealed class MonitorFailureException : Exception
{
    public MonitorFailureException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Abstraction for a SCPI command client.
/// </summary>
public interface IScpiCommandClient
{
    /// <summary>
    /// Executes a SCPI command against the specified instrument.
    /// </summary>
    Task<string> SendAsync(string instrumentId, string command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options controlling the periodic monitoring tasks.
/// </summary>
public sealed record InstrumentMonitorOptions
{
    public const string SectionName = "Monitoring";

    public IReadOnlyList<string> Instruments { get; init; } = Array.Empty<string>();

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan SelfCheckInterval { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan TemperatureInterval { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan HumidityInterval { get; init; } = TimeSpan.FromMinutes(1);

    public string HeartbeatCommand { get; init; } = "*IDN?";

    public string SelfCheckCommand { get; init; } = "SELF:CHECK?";

    public string TemperatureCommand { get; init; } = "MEAS:TEMP?";

    public string HumidityCommand { get; init; } = "MEAS:HUM?";

    public void Validate()
    {
        if (Instruments.Count == 0)
        {
            throw new InvalidOperationException("At least one instrument must be configured for monitoring.");
        }

        ValidateInterval(HeartbeatInterval, nameof(HeartbeatInterval));
        ValidateInterval(SelfCheckInterval, nameof(SelfCheckInterval));
        ValidateInterval(TemperatureInterval, nameof(TemperatureInterval));
        ValidateInterval(HumidityInterval, nameof(HumidityInterval));
    }

    private static void ValidateInterval(TimeSpan value, string propertyName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(propertyName, value, "Interval must be positive.");
        }
    }
}

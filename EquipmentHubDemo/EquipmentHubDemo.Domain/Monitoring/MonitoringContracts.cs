namespace EquipmentHubDemo.Domain.Monitoring;

/// <summary>
/// The different monitoring tasks executed against an instrument.
/// </summary>
public enum MonitorTaskType
{
    Heartbeat,
    SelfCheck,
    Temperature,
    Humidity,
}

/// <summary>
/// Describes an instrument specific SCPI command that should be executed.
/// </summary>
/// <param name="InstrumentId">The identifier of the instrument that should receive the command.</param>
/// <param name="Command">The SCPI command to execute.</param>
/// <param name="TaskType">The type of monitoring task represented by the command.</param>
public sealed record MonitorCommand(string InstrumentId, string Command, MonitorTaskType TaskType);

/// <summary>
/// Result of executing a monitoring command.
/// </summary>
/// <param name="Command">The command that was executed.</param>
/// <param name="TimestampUtc">The UTC timestamp when the command completed.</param>
/// <param name="Success">Whether the command completed successfully.</param>
/// <param name="Response">The raw SCPI response.</param>
/// <param name="NumericValue">Optional numeric value extracted from the response (temperature/humidity).</param>
/// <param name="Error">Optional error message in case the command failed.</param>
public sealed record MonitorCommandResult(
    MonitorCommand Command,
    DateTime TimestampUtc,
    bool Success,
    string Response,
    double? NumericValue,
    string? Error);

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

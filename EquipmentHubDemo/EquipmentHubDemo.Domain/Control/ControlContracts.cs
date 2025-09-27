namespace EquipmentHubDemo.Domain.Control;

/// <summary>
/// Describes a high-level scenario that should be applied to an instrument.
/// </summary>
/// <param name="ScenarioName">The user friendly scenario name.</param>
/// <param name="InstrumentId">The instrument identifier.</param>
/// <param name="Parameters">Scenario specific SCPI parameters (name/value pairs).</param>
public sealed record InstrumentScenario(string ScenarioName, string InstrumentId, IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Encapsulates the result of an instrument control operation.
/// </summary>
/// <param name="InstrumentId">The instrument identifier.</param>
/// <param name="Operation">The operation performed.</param>
/// <param name="TimestampUtc">UTC timestamp when the operation completed.</param>
/// <param name="Details">Additional contextual information.</param>
public sealed record ControlOperationResult(string InstrumentId, string Operation, DateTime TimestampUtc, string Details);

public interface IInstrumentConfigurationService
{
    Task<ControlOperationResult> ConfigureAsync(InstrumentScenario scenario, CancellationToken cancellationToken = default);
}

public interface IInstrumentCalibrationService
{
    Task<ControlOperationResult> ScheduleYearlyCalibrationAsync(string instrumentId, DateOnly scheduledDate, CancellationToken cancellationToken = default);
}

public interface IRfPathService
{
    Task<ControlOperationResult> NormalizeAsync(string instrumentId, CancellationToken cancellationToken = default);

    Task<ControlOperationResult> VerifyAsync(string instrumentId, CancellationToken cancellationToken = default);
}

public interface INetworkTrafficOptimizer
{
    Task<ControlOperationResult> OptimizeAsync(string clusterName, CancellationToken cancellationToken = default);
}

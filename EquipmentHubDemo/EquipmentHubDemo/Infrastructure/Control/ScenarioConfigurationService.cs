using EquipmentHubDemo.Domain.Control;
using EquipmentHubDemo.Domain.Monitoring;
using Microsoft.Extensions.Logging;

namespace EquipmentHubDemo.Infrastructure.Control;

public sealed class ScenarioConfigurationService : IInstrumentConfigurationService
{
    private readonly IScpiCommandClient _scpiCommandClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScenarioConfigurationService> _logger;

    public ScenarioConfigurationService(
        IScpiCommandClient scpiCommandClient,
        TimeProvider timeProvider,
        ILogger<ScenarioConfigurationService> logger)
    {
        _scpiCommandClient = scpiCommandClient ?? throw new ArgumentNullException(nameof(scpiCommandClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ControlOperationResult> ConfigureAsync(InstrumentScenario scenario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        foreach (var parameter in scenario.Parameters)
        {
            var command = $"CONF:{parameter.Key} {parameter.Value}";
            await _scpiCommandClient.SendAsync(scenario.InstrumentId, command, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Applied scenario {Scenario} parameter {Parameter}={Value} to instrument {Instrument}.",
                scenario.ScenarioName,
                parameter.Key,
                parameter.Value,
                scenario.InstrumentId);
        }

        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;
        return new ControlOperationResult(
            scenario.InstrumentId,
            $"Scenario:{scenario.ScenarioName}",
            timestamp,
            $"Configured {scenario.Parameters.Count} parameter(s).");
    }
}

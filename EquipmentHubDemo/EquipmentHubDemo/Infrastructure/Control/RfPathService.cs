using EquipmentHubDemo.Domain.Control;
using EquipmentHubDemo.Domain.Monitoring;
using Microsoft.Extensions.Logging;

namespace EquipmentHubDemo.Infrastructure.Control;

public sealed class RfPathService : IRfPathService
{
    private readonly IScpiCommandClient _scpiCommandClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RfPathService> _logger;

    public RfPathService(
        IScpiCommandClient scpiCommandClient,
        TimeProvider timeProvider,
        ILogger<RfPathService> logger)
    {
        _scpiCommandClient = scpiCommandClient ?? throw new ArgumentNullException(nameof(scpiCommandClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ControlOperationResult> NormalizeAsync(string instrumentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);

        var response = await _scpiCommandClient.SendAsync(instrumentId, "RF:NORM", cancellationToken).ConfigureAwait(false);
        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;
        _logger.LogInformation("RF path normalization completed for {Instrument} with response {Response}.", instrumentId, response);

        return new ControlOperationResult(instrumentId, "RF:Normalize", timestamp, response);
    }

    public async Task<ControlOperationResult> VerifyAsync(string instrumentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);

        var response = await _scpiCommandClient.SendAsync(instrumentId, "RF:VER?", cancellationToken).ConfigureAwait(false);
        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;

        if (!string.Equals(response, "PASS", StringComparison.OrdinalIgnoreCase))
        {
            throw new MonitorFailureException($"RF verification returned '{response}' for instrument '{instrumentId}'.");
        }

        _logger.LogInformation("RF path verification passed for {Instrument}.", instrumentId);
        return new ControlOperationResult(instrumentId, "RF:Verify", timestamp, response);
    }
}

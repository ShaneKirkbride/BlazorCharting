using EquipmentHubDemo.Domain.Control;
using EquipmentHubDemo.Domain.Monitoring;
using Microsoft.Extensions.Logging;

namespace EquipmentHubDemo.Infrastructure.Control;

public sealed class InstrumentCalibrationService : IInstrumentCalibrationService
{
    private readonly IScpiCommandClient _scpiCommandClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InstrumentCalibrationService> _logger;

    public InstrumentCalibrationService(
        IScpiCommandClient scpiCommandClient,
        TimeProvider timeProvider,
        ILogger<InstrumentCalibrationService> logger)
    {
        _scpiCommandClient = scpiCommandClient ?? throw new ArgumentNullException(nameof(scpiCommandClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ControlOperationResult> ScheduleYearlyCalibrationAsync(string instrumentId, DateOnly scheduledDate, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);

        await _scpiCommandClient.SendAsync(instrumentId, "CAL:INIT", cancellationToken).ConfigureAwait(false);
        await _scpiCommandClient.SendAsync(instrumentId, "CAL:STORE", cancellationToken).ConfigureAwait(false);

        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;
        var message = $"Calibration stored for {scheduledDate:yyyy-MM-dd}.";
        _logger.LogInformation("Scheduled yearly calibration for {Instrument} on {Date}.", instrumentId, scheduledDate);

        return new ControlOperationResult(instrumentId, "Calibration", timestamp, message);
    }
}

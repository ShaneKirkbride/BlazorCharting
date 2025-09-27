using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Instrumentation;

namespace Agent;

internal sealed class MeasurementGenerator : IMeasurementGenerator
{
    private static readonly HashSet<string> MonitorMetrics = new(
        new[] { "Temperature", "Humidity", "Heartbeat", "SelfCheck" },
        StringComparer.OrdinalIgnoreCase);

    private readonly AgentOptions _options;
    private readonly InstrumentMonitorOptions _monitorOptions;
    private readonly IScpiCommandClient _scpiClient;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly ILogger<MeasurementGenerator> _logger;
    private readonly ConcurrentDictionary<string, MonitorSchedule> _schedules;

    public MeasurementGenerator(
        IOptions<AgentOptions> options,
        IOptions<InstrumentMonitorOptions> monitorOptions,
        IScpiCommandClient scpiClient,
        TimeProvider timeProvider,
        Random random,
        ILogger<MeasurementGenerator> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        var monitorValue = monitorOptions?.Value ?? throw new ArgumentNullException(nameof(monitorOptions));
        monitorValue.Validate();
        _monitorOptions = monitorValue;

        _scpiClient = scpiClient ?? throw new ArgumentNullException(nameof(scpiClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _schedules = new ConcurrentDictionary<string, MonitorSchedule>(StringComparer.OrdinalIgnoreCase);
        foreach (var instrument in _monitorOptions.Instruments)
        {
            _schedules.TryAdd(instrument, MonitorSchedule.Create(now));
        }
    }

    public IEnumerable<Measurement> CreateMeasurements(DateTime timestampUtc)
    {
        foreach (var instrumentId in _monitorOptions.Instruments)
        {
            foreach (var measurement in ExecuteMonitorTasks(instrumentId, timestampUtc))
            {
                yield return measurement;
            }
        }

        var angle = timestampUtc.Ticks / 2e7d;
        foreach (var instrument in _options.Instruments)
        {
            foreach (var metric in instrument.Metrics)
            {
                if (IsMonitorMetric(metric))
                {
                    continue;
                }

                yield return CreateSyntheticMeasurement(instrument.InstrumentId, metric, timestampUtc, angle);
            }
        }
    }

    private IEnumerable<Measurement> ExecuteMonitorTasks(string instrumentId, DateTime timestampUtc)
    {
        var schedule = _schedules.GetOrAdd(instrumentId, _ => MonitorSchedule.Create(timestampUtc));

        if (timestampUtc >= schedule.NextHeartbeatUtc)
        {
            yield return ExecuteStatusCommand(
                instrumentId,
                _monitorOptions.HeartbeatCommand,
                "Heartbeat",
                timestampUtc);
            schedule.NextHeartbeatUtc = timestampUtc + _monitorOptions.HeartbeatInterval;
        }

        if (timestampUtc >= schedule.NextSelfCheckUtc)
        {
            yield return ExecuteStatusCommand(
                instrumentId,
                _monitorOptions.SelfCheckCommand,
                "SelfCheck",
                timestampUtc);
            schedule.NextSelfCheckUtc = timestampUtc + _monitorOptions.SelfCheckInterval;
        }

        if (timestampUtc >= schedule.NextTemperatureUtc)
        {
            yield return ExecuteNumericCommand(
                instrumentId,
                _monitorOptions.TemperatureCommand,
                "Temperature",
                timestampUtc);
            schedule.NextTemperatureUtc = timestampUtc + _monitorOptions.TemperatureInterval;
        }

        if (timestampUtc >= schedule.NextHumidityUtc)
        {
            yield return ExecuteNumericCommand(
                instrumentId,
                _monitorOptions.HumidityCommand,
                "Humidity",
                timestampUtc);
            schedule.NextHumidityUtc = timestampUtc + _monitorOptions.HumidityInterval;
        }
    }

    private Measurement ExecuteStatusCommand(
        string instrumentId,
        string command,
        string metric,
        DateTime timestampUtc)
    {
        var response = SendCommand(instrumentId, command);
        _logger.LogInformation(
            "Executed {Metric} command {Command} on {Instrument} with response '{Response}'.",
            metric,
            command,
            instrumentId,
            response);

        return new Measurement(new MeasureKey(instrumentId, metric), 1d, timestampUtc);
    }

    private Measurement ExecuteNumericCommand(
        string instrumentId,
        string command,
        string metric,
        DateTime timestampUtc)
    {
        var response = SendCommand(instrumentId, command);
        if (!double.TryParse(response, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new MonitorFailureException($"Unable to parse numeric response '{response}' for command '{command}'.");
        }

        _logger.LogInformation(
            "{Metric} for {Instrument} = {Value:F3} (response '{Response}').",
            metric,
            instrumentId,
            value,
            response);

        return new Measurement(new MeasureKey(instrumentId, metric), value, timestampUtc);
    }

    private string SendCommand(string instrumentId, string command)
    {
        try
        {
            return _scpiClient.SendAsync(instrumentId, command).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SCPI command {Command} failed for instrument {Instrument}.", command, instrumentId);
            throw new MonitorFailureException($"Command '{command}' failed for instrument '{instrumentId}'.", ex);
        }
    }

    private Measurement CreateSyntheticMeasurement(string instrumentId, string metric, DateTime timestampUtc, double angle)
    {
        var trimmedMetric = metric?.Trim() ?? string.Empty;
        var key = new MeasureKey(instrumentId, trimmedMetric);
        var value = trimmedMetric.Length == 0
            ? GenerateFallbackValue(trimmedMetric)
            : GenerateSyntheticValue(trimmedMetric, angle);

        return new Measurement(key, value, timestampUtc);
    }

    private double GenerateSyntheticValue(string metric, double angle)
    {
        if (string.Equals(metric, "Power (240VAC)", StringComparison.OrdinalIgnoreCase))
        {
            return 240 + 5 * Math.Sin(angle) + 2 * _random.NextDouble();
        }

        return GenerateFallbackValue(metric);
    }

    private double GenerateFallbackValue(string metric)
    {
        var value = _random.NextDouble();
        _logger.LogDebug("No generator registered for metric {Metric}; using random value {Value:F3}.", metric, value);
        return value;
    }

    private static bool IsMonitorMetric(string metric) => MonitorMetrics.Contains(metric ?? string.Empty);

    private sealed class MonitorSchedule
    {
        public DateTime NextHeartbeatUtc { get; set; }
        public DateTime NextSelfCheckUtc { get; set; }
        public DateTime NextTemperatureUtc { get; set; }
        public DateTime NextHumidityUtc { get; set; }

        public static MonitorSchedule Create(DateTime startUtc)
            => new()
            {
                NextHeartbeatUtc = startUtc,
                NextSelfCheckUtc = startUtc,
                NextTemperatureUtc = startUtc,
                NextHumidityUtc = startUtc
            };
    }
}

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

    private readonly InstrumentMonitorOptions _monitorOptions;
    private readonly IScpiCommandClient _scpiClient;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly ILogger<MeasurementGenerator> _logger;
    private readonly ConcurrentDictionary<string, MonitorSchedule> _schedules;
    private readonly ConcurrentDictionary<MeasureKey, SyntheticSeriesState> _syntheticSeries;
    private readonly InstrumentOptions _instrument;
    private readonly string _instrumentId;

    public MeasurementGenerator(
        IOptions<AgentOptions> options,
        IOptions<InstrumentMonitorOptions> monitorOptions,
        IScpiCommandClient scpiClient,
        TimeProvider timeProvider,
        Random random,
        ILogger<MeasurementGenerator> logger)
    {
        var agentOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _instrument = agentOptions.Instrument ?? throw new InvalidOperationException("Agent instrument must be configured.");
        _instrumentId = _instrument.InstrumentId;

        var monitorValue = monitorOptions?.Value ?? throw new ArgumentNullException(nameof(monitorOptions));
        monitorValue.Validate();
        _monitorOptions = monitorValue;

        _scpiClient = scpiClient ?? throw new ArgumentNullException(nameof(scpiClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _schedules = new ConcurrentDictionary<string, MonitorSchedule>(StringComparer.OrdinalIgnoreCase);
        _schedules.TryAdd(_instrumentId, MonitorSchedule.Create(now));

        _syntheticSeries = new ConcurrentDictionary<MeasureKey, SyntheticSeriesState>();

        if (!_monitorOptions.Instruments.Any(id => string.Equals(id, _instrumentId, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Monitoring configuration does not include instrument {InstrumentId}. Falling back to agent instrument only.",
                _instrumentId);
        }
    }

    public IEnumerable<Measurement> CreateMeasurements(DateTime timestampUtc)
    {
        foreach (var measurement in ExecuteMonitorTasks(_instrumentId, timestampUtc))
        {
            yield return measurement;
        }

        var angle = timestampUtc.Ticks / 2e7d;
        foreach (var metric in _instrument.Metrics)
        {
            if (IsMonitorMetric(metric))
            {
                continue;
            }

            yield return CreateSyntheticMeasurement(_instrumentId, metric, timestampUtc, angle);
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
            : GenerateSyntheticValue(key, angle);

        return new Measurement(key, value, timestampUtc);
    }

    private double GenerateSyntheticValue(MeasureKey key, double angle)
    {
        if (string.Equals(key.Metric, "Power (240VAC)", StringComparison.OrdinalIgnoreCase))
        {
            var series = _syntheticSeries.GetOrAdd(key, _ => SyntheticSeriesState.Create(_random));
            return series.NextPowerSample(angle, _random);
        }

        return GenerateFallbackValue(key.Metric);
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

    private sealed class SyntheticSeriesState
    {
        private double _phaseOffset;
        private double _lowFrequencyPhase;
        private double _drift;

        private SyntheticSeriesState(double phaseOffset, double lowFrequencyPhase)
        {
            _phaseOffset = phaseOffset;
            _lowFrequencyPhase = lowFrequencyPhase;
        }

        public static SyntheticSeriesState Create(Random random)
        {
            ArgumentNullException.ThrowIfNull(random);
            return new SyntheticSeriesState(
                phaseOffset: random.NextDouble() * Math.PI * 2,
                lowFrequencyPhase: random.NextDouble() * Math.PI * 2);
        }

        public double NextPowerSample(double angle, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            _phaseOffset += 0.01 + random.NextDouble() * 0.015;
            _lowFrequencyPhase += 0.002 + random.NextDouble() * 0.004;
            _drift = Math.Clamp(_drift + (random.NextDouble() - 0.5) * 0.25, -6, 6);

            var waveform = 240
                + 7.5 * Math.Sin(angle + _phaseOffset)
                + 3.0 * Math.Sin(_lowFrequencyPhase);

            var jitter = (random.NextDouble() - 0.5) * 3.0;

            return waveform + _drift + jitter;
        }
    }
}

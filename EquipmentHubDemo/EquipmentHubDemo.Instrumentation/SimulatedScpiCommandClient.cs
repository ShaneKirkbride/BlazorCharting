using System.Collections.Concurrent;
using System.Linq;
using EquipmentHubDemo.Domain.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Instrumentation;

public sealed record SimulatedInstrumentProfile
{
    public string InstrumentId { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = "EquipmentHub";

    public string Model { get; init; } = "SpectrumAnalyzer";

    public string SerialNumber { get; init; } = "0000";

    public double BaseTemperatureCelsius { get; init; } = 25.0;

    public double BaseHumidityPercent { get; init; } = 45.0;
}

public sealed record SimulatedScpiOptions
{
    public const string SectionName = "SimulatedScpi";

    public IReadOnlyList<SimulatedInstrumentProfile> Instruments { get; init; } = Array.Empty<SimulatedInstrumentProfile>();

    public double TemperatureNoiseAmplitude { get; init; } = 0.4;

    public double HumidityNoiseAmplitude { get; init; } = 1.5;
}

/// <summary>
/// Lightweight SCPI simulation used by monitoring and control services.
/// </summary>
public sealed class SimulatedScpiCommandClient : IScpiCommandClient
{
    private readonly ILogger<SimulatedScpiCommandClient> _logger;
    private readonly IReadOnlyDictionary<string, InstrumentState> _state;
    private readonly double _temperatureNoiseAmplitude;
    private readonly double _humidityNoiseAmplitude;
    private readonly Random _random;

    public SimulatedScpiCommandClient(
        IOptions<SimulatedScpiOptions> options,
        ILogger<SimulatedScpiCommandClient> logger,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = random ?? throw new ArgumentNullException(nameof(random));

        var value = options.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(options));
        if (value.Instruments.Count == 0)
        {
            throw new InvalidOperationException("Simulated SCPI client requires at least one instrument profile.");
        }

        _temperatureNoiseAmplitude = Math.Abs(value.TemperatureNoiseAmplitude);
        _humidityNoiseAmplitude = Math.Abs(value.HumidityNoiseAmplitude);

        _state = value.Instruments.ToDictionary(
            profile => profile.InstrumentId,
            profile => new InstrumentState(profile, _random),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<string> SendAsync(string instrumentId, string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(command);

        if (!_state.TryGetValue(instrumentId, out var state))
        {
            throw new MonitorFailureException($"Unknown instrument '{instrumentId}'.");
        }

        lock (state)
        {
            return Task.FromResult(ExecuteInternal(state, command));
        }
    }

    private string ExecuteInternal(InstrumentState state, string command)
    {
        _logger.LogDebug("Executing simulated SCPI command {Command} on instrument {Instrument}.", command, state.Profile.InstrumentId);

        return command.ToUpperInvariant() switch
        {
            "*IDN?" => $"{state.Profile.Manufacturer},{state.Profile.Model},{state.Profile.SerialNumber},1.0",
            "SELF:CHECK?" => state.LastSelfCheck = $"0,OK,{DateTime.UtcNow:O}",
            "MEAS:TEMP?" => FormatValue(state.NextTemperature(_temperatureNoiseAmplitude, _random)),
            "MEAS:HUM?" => FormatValue(state.NextHumidity(_humidityNoiseAmplitude, _random)),
            "CAL:INIT" => state.MarkCalibrationStarted(),
            "CAL:STORE" => state.MarkCalibrationComplete(),
            "RF:NORM" => state.MarkRfNormalization(),
            "RF:VER?" => state.GetVerificationResult(),
            var cmd when cmd.StartsWith("CONF:", StringComparison.OrdinalIgnoreCase) => state.ApplyConfiguration(cmd),
            _ => throw new MonitorFailureException($"Unsupported SCPI command '{command}'.")
        };
    }

    private string FormatValue(double value) => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

    private sealed class InstrumentState
    {
        private readonly ConcurrentDictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);
        private double _temperaturePhase;
        private double _humidityPhase;
        private double _temperatureDrift;
        private double _humidityDrift;

        public InstrumentState(SimulatedInstrumentProfile profile, Random random)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            ArgumentNullException.ThrowIfNull(random);

            _temperaturePhase = random.NextDouble() * Math.PI * 2;
            _humidityPhase = random.NextDouble() * Math.PI * 2;
        }

        public SimulatedInstrumentProfile Profile { get; }

        public string LastSelfCheck { get; set; } = "0,OK";

        public string? LastCalibration { get; private set; }

        public DateTime? LastNormalizationUtc { get; private set; }

        public double NextTemperature(double amplitude, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            _temperaturePhase += 0.04 + random.NextDouble() * 0.03;
            _temperatureDrift = CalculateDrift(_temperatureDrift, random, maxMagnitude: 1.2);

            var waveform = Math.Sin(_temperaturePhase) * Math.Max(amplitude, 0);
            var jitter = (random.NextDouble() - 0.5) * Math.Max(amplitude, 0.6);

            return Profile.BaseTemperatureCelsius + waveform + _temperatureDrift + jitter;
        }

        public double NextHumidity(double amplitude, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            _humidityPhase += 0.03 + random.NextDouble() * 0.02;
            _humidityDrift = CalculateDrift(_humidityDrift, random, maxMagnitude: 3.0);

            var waveform = Math.Sin(_humidityPhase) * Math.Max(amplitude, 0);
            var jitter = (random.NextDouble() - 0.5) * Math.Max(amplitude, 1.0);

            return Profile.BaseHumidityPercent + waveform + _humidityDrift + jitter;
        }

        public string ApplyConfiguration(string command)
        {
            var payload = command[5..].Trim();
            var separatorIndex = payload.IndexOf(' ');
            if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
            {
                throw new MonitorFailureException($"Invalid configuration command '{command}'.");
            }

            var key = payload[..separatorIndex].Trim();
            var value = payload[(separatorIndex + 1)..].Trim();
            _parameters[key] = value;
            return $"{key}={value}";
        }

        public string MarkCalibrationStarted()
        {
            LastCalibration = $"STARTED:{DateTime.UtcNow:O}";
            return LastCalibration;
        }

        public string MarkCalibrationComplete()
        {
            LastCalibration = $"COMPLETE:{DateTime.UtcNow:O}";
            return LastCalibration;
        }

        public string MarkRfNormalization()
        {
            LastNormalizationUtc = DateTime.UtcNow;
            return $"NORM:{LastNormalizationUtc:O}";
        }

        public string GetVerificationResult()
        {
            var normalized = LastNormalizationUtc.HasValue && DateTime.UtcNow - LastNormalizationUtc < TimeSpan.FromHours(12);
            return normalized ? "PASS" : "WARN";
        }

        private static double CalculateDrift(double current, Random random, double maxMagnitude)
        {
            ArgumentNullException.ThrowIfNull(random);
            var nudged = current + (random.NextDouble() - 0.5) * 0.2;
            return Math.Clamp(nudged * 0.98, -Math.Abs(maxMagnitude), Math.Abs(maxMagnitude));
        }
    }
}

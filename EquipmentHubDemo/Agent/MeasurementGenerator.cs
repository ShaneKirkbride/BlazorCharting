using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent;

internal sealed class MeasurementGenerator : IMeasurementGenerator
{
    private readonly AgentOptions _options;
    private readonly Random _random;
    private readonly ILogger<MeasurementGenerator> _logger;

    public MeasurementGenerator(IOptions<AgentOptions> options, Random random, ILogger<MeasurementGenerator> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IEnumerable<Measurement> CreateMeasurements(DateTime timestampUtc)
    {
        var angle = timestampUtc.Ticks / 2e7d;
        foreach (var instrument in _options.Instruments)
        {
            foreach (var metric in instrument.Metrics)
            {
                yield return CreateMeasurement(instrument.InstrumentId, metric, timestampUtc, angle);
            }
        }
    }

    private Measurement CreateMeasurement(string instrumentId, string metric, DateTime timestampUtc, double angle)
    {
        var trimmedMetric = metric?.Trim() ?? string.Empty;
        var key = new MeasureKey(instrumentId, trimmedMetric);
        var value = trimmedMetric.Length == 0
            ? GenerateFallbackValue(trimmedMetric)
            : GenerateValue(trimmedMetric, angle);

        return new Measurement(key, value, timestampUtc);
    }

    private double GenerateValue(string metric, double angle)
    {
        if (string.Equals(metric, "Temperature", StringComparison.OrdinalIgnoreCase))
        {
            return 22 + 5 * Math.Sin(angle) + 0.5 * _random.NextDouble();
        }

        if (string.Equals(metric, "Humidity", StringComparison.OrdinalIgnoreCase))
        {
            return 45 + 15 * Math.Cos(angle) + _random.NextDouble();
        }

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
}

using System.Linq;
using EquipmentHubDemo.Domain.Predict;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Infrastructure.Predict;

public sealed record PredictiveDiagnosticsOptions
{
    public const string SectionName = "PredictiveDiagnostics";

    public TimeSpan LookbackWindow { get; init; } = TimeSpan.FromHours(12);
}

public sealed class PredictiveDiagnosticsService : IPredictiveDiagnosticsService
{
    private readonly IDiagnosticRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly PredictiveDiagnosticsOptions _options;
    private readonly ILogger<PredictiveDiagnosticsService> _logger;

    public PredictiveDiagnosticsService(
        IDiagnosticRepository repository,
        IOptions<PredictiveDiagnosticsOptions> options,
        TimeProvider timeProvider,
        ILogger<PredictiveDiagnosticsService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.LookbackWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.LookbackWindow, "Lookback window must be positive.");
        }
    }

    public async Task<PredictiveInsight> GetInsightAsync(string instrumentId, string metric, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);

        var samples = await _repository.GetRecentAsync(instrumentId, metric, _options.LookbackWindow, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (samples.Count == 0)
        {
            _logger.LogInformation("No diagnostic samples available for {Instrument}/{Metric}.", instrumentId, metric);
            return new PredictiveInsight(instrumentId, metric, 0, now, 0, 0);
        }

        var values = samples.Select(s => s.Value).ToArray();
        var mean = values.Average();
        var variance = values.Length <= 1
            ? 0
            : values.Select(v => Math.Pow(v - mean, 2)).Sum() / values.Length;
        var stdDev = Math.Sqrt(variance);
        var failureProbability = Math.Clamp(stdDev / (Math.Abs(mean) + 0.001), 0, 1);

        return new PredictiveInsight(instrumentId, metric, failureProbability, now, mean, stdDev);
    }
}

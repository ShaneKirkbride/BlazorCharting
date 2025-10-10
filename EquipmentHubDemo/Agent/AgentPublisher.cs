using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetMQ;
using NetMQ.Sockets;

namespace Agent;

internal sealed class AgentPublisher : BackgroundService
{
    private readonly AgentOptions _options;
    private readonly IMeasurementGenerator _generator;
    private readonly ILogger<AgentPublisher> _logger;

    public AgentPublisher(IOptions<AgentOptions> options, IMeasurementGenerator generator, ILogger<AgentPublisher> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AsyncIO.ForceDotNet.Force();
        using var publisher = new PublisherSocket();
        publisher.Options.Linger = TimeSpan.Zero;
        publisher.Options.SendHighWatermark = _options.SendHighWatermark;
        publisher.Connect(_options.PublishEndpoint);

        _logger.LogInformation(
            "Publishing {InstrumentCount} instrument(s) to {Endpoint} on topic {Topic}.",
            _options.Instruments.Count,
            _options.PublishEndpoint,
            _options.Topic);

        using var timer = new PeriodicTimer(_options.PublishInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var timestamp = DateTime.UtcNow;
                foreach (var measurement in _generator.CreateMeasurements(timestamp))
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await PublishWithRetryAsync(publisher, measurement, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Agent publisher cancelled.");
        }
        finally
        {
            NetMQConfig.Cleanup();
            _logger.LogInformation("Agent publisher stopped.");
        }
    }

    private async Task PublishWithRetryAsync(PublisherSocket publisher, Measurement measurement, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(measurement, MeasurementJson.Options);
        var attempt = 1;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (TrySendMeasurement(publisher, payload))
                {
                    _logger.LogDebug(
                        "Published measurement {Key} at {TimestampUtc:o} with value {Value:F3}.",
                        measurement.Key,
                        measurement.TimestampUtc,
                        measurement.Value);
                    return;
                }

                _logger.LogWarning(
                    "Publishing measurement {Key} timed out after waiting {Timeout}.",
                    measurement.Key,
                    _options.SendTimeout);
            }
            catch (NetMQException ex) when (attempt < _options.SendRetryCount)
            {
                _logger.LogWarning(
                    ex,
                    "Attempt {Attempt} to publish measurement {Key} failed. Retrying in {Delay} ms.",
                    attempt,
                    measurement.Key,
                    _options.SendRetryBackoffMilliseconds);
            }
            catch (NetMQException ex)
            {
                _logger.LogError(ex, "Failed to publish measurement {Key} after {Attempt} attempt(s).", measurement.Key, attempt);
                return;
            }

            if (attempt >= _options.SendRetryCount)
            {
                _logger.LogWarning(
                    "Dropping measurement {Key} after {Attempt} timed out publish attempts.",
                    measurement.Key,
                    attempt);
                return;
            }

            attempt++;
            await Task.Delay(_options.RetryBackoff, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TrySendMeasurement(PublisherSocket publisher, string payload)
    {
        var message = new NetMQMessage(2);
        message.Append(_options.Topic);
        message.Append(payload);

        return publisher.TrySendMultipartMessage(_options.SendTimeout, message);
    }
}

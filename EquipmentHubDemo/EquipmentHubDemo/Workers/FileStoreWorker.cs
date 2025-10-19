using System;
using System.Text.Json;
using EquipmentHubDemo.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetMQ;
using NetMQ.Sockets;
using static EquipmentHubDemo.Messaging.Zmq;

namespace EquipmentHubDemo.Workers;

/// <summary>
/// Subscribes to "measure", applies a simple delay filter, saves to LiteDB (latest + history), republishes "filtered".
/// </summary>
public sealed class FilterStoreWorker : BackgroundService
{
    private readonly IMeasurementPipeline _pipeline;
    private readonly ILogger<FilterStoreWorker> _logger;

    public FilterStoreWorker(IMeasurementPipeline pipeline, ILogger<FilterStoreWorker> logger)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task ExecuteAsync(CancellationToken ct)
        => Task.Factory.StartNew(
            async () =>
            {
                AsyncIO.ForceDotNet.Force();

                using var sub = new SubscriberSocket();
                sub.Connect(SubConnect);
                sub.Subscribe(TopicMeasure);

                using var pub = new PublisherSocket();
                pub.Connect(PubConnect);
                NetMQMessage? msg = null;
                while (!ct.IsCancellationRequested)
                {
                    if (!sub.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref msg) || msg is null || msg.FrameCount < 2)
                    {
                        continue;
                    }

                    var payload = msg[1].ConvertToString();

                    Measurement? measurement;
                    try
                    {
                        measurement = JsonSerializer.Deserialize<Measurement>(payload, JsonOptions.Default);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Discarded malformed measurement payload.");
                        continue;
                    }

                    if (measurement is null)
                    {
                        _logger.LogWarning("Discarded empty measurement payload.");
                        continue;
                    }

                    FilteredMeasurement filtered;
                    try
                    {
                        filtered = await _pipeline.ProcessAsync(measurement, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Filter/store pipeline failed for {Key}.", measurement.Key.ToString());
                        continue;
                    }

                    var outJson = JsonSerializer.Serialize(filtered, JsonOptions.Default);
                    var outMsg = new NetMQMessage(2);
                    outMsg.Append(TopicFiltered);
                    outMsg.Append(outJson);
                    pub.SendMultipartMessage(outMsg);
                }
            },
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .Unwrap();
}

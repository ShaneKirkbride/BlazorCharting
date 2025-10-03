using System.Text.Json;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Infrastructure;
using EquipmentHubDemo.Domain.Predict;
using Microsoft.Extensions.Hosting;
using NetMQ;
using NetMQ.Sockets;
using static EquipmentHubDemo.Messaging.Zmq;

namespace EquipmentHubDemo.Workers;

/// <summary>
/// Subscribes to "measure", applies a simple delay filter, saves to LiteDB (latest + history), republishes "filtered".
/// </summary>
public sealed class FilterStoreWorker : BackgroundService
{
    private readonly IMeasurementRepository _repo;
    private readonly IDiagnosticRepository _diagnostics;

    public FilterStoreWorker(IMeasurementRepository repo, IDiagnosticRepository diagnostics)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

                    // 👇 use shared options
                    var m = JsonSerializer.Deserialize<Measurement>(payload, JsonOptions.Default);
                    if (m is null) continue;

                    try
                    {
                        await Task.Delay(200, ct).ConfigureAwait(false); // demo delay
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    var f = new FilteredMeasurement(m.Key, m.Value, DateTime.UtcNow);

                    _repo.AppendHistory(f);
                    _repo.UpsertLatest(f);

                    await AppendDiagnosticsAsync(f, ct).ConfigureAwait(false);

                    var outJson = JsonSerializer.Serialize(f, JsonOptions.Default);
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

    private static bool ShouldRecordDiagnostics(string metric)
        => string.Equals(metric, "Temperature", StringComparison.OrdinalIgnoreCase)
            || string.Equals(metric, "Humidity", StringComparison.OrdinalIgnoreCase);

    private async Task AppendDiagnosticsAsync(FilteredMeasurement measurement, CancellationToken ct)
    {
        if (!ShouldRecordDiagnostics(measurement.Key.Metric))
        {
            return;
        }

        var sample = new DiagnosticSample(
            measurement.Key.InstrumentId,
            measurement.Key.Metric,
            measurement.Value,
            measurement.TimestampUtc);

        await _diagnostics.AddAsync(sample, ct).ConfigureAwait(false);
    }
}

using System.Text.Json;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Infrastructure;
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

    public FilterStoreWorker(IMeasurementRepository repo) => _repo = repo;

    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(async () =>
    {
        AsyncIO.ForceDotNet.Force();

        using var sub = new SubscriberSocket();
        sub.Connect(SubConnect);
        sub.Subscribe(TopicMeasure);

        using var pub = new PublisherSocket();
        pub.Connect(PubConnect);
        NetMQMessage? msg = new NetMQMessage();
        while (!ct.IsCancellationRequested)
        {
            if (!sub.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref msg)) continue;

            var payload = msg[1].ConvertToString();

            // 👇 use shared options
            var m = JsonSerializer.Deserialize<Measurement>(payload, JsonOptions.Default);
            if (m is null) continue;

            await Task.Delay(200, ct); // demo delay

            var f = new FilteredMeasurement(m.Key, m.Value, DateTime.UtcNow);

            _repo.AppendHistory(f);
            _repo.UpsertLatest(f);

            var outJson = JsonSerializer.Serialize(f, JsonOptions.Default);
            var outMsg = new NetMQMessage(2);
            outMsg.Append(TopicFiltered);
            outMsg.Append(outJson);
            pub.SendMultipartMessage(outMsg);
        }
    }, ct);
}

using EquipmentHubDemo.Domain;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;
using System.Text.Json;

using static EquipmentHubDemo.Messaging.Zmq;

namespace EquipmentHubDemo.Live;

/// <summary>
/// In-memory cache of latest time series for the UI.
/// </summary>
public sealed class LiveCache
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(DateTime X, double Y)>> _series = new();
    private const int MaxPointsPerKey = 2_000;

    public event Action? Updated;

    public IReadOnlyList<string> Keys => _series.Keys.OrderBy(k => k).ToArray();

    public IReadOnlyList<(DateTime X, double Y)> GetSeries(string key)
        => _series.TryGetValue(key, out var q) ? q.ToArray() : Array.Empty<(DateTime, double)>();

    internal void Push(string key, DateTime x, double y)
    {
        var q = _series.GetOrAdd(key, _ => new ConcurrentQueue<(DateTime, double)>());
        q.Enqueue((x, y));
        while (q.Count > MaxPointsPerKey && q.TryDequeue(out _)) { }
        Updated?.Invoke();
    }
}

public sealed class LiveSubscriberWorker : BackgroundService
{
    private readonly LiveCache _cache;

    public LiveSubscriberWorker(LiveCache cache) => _cache = cache;

    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() =>
    {
        AsyncIO.ForceDotNet.Force();

        using var sub = new SubscriberSocket();
        sub.Connect(SubConnect);
        sub.Subscribe(TopicFiltered);
        NetMQMessage msg = new NetMQMessage();

        while (!ct.IsCancellationRequested)
        {
            if (!sub.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref msg)) continue;

            var json = msg[1].ConvertToString();

            // 👇 use shared options
            var f = JsonSerializer.Deserialize<FilteredMeasurement>(json, JsonOptions.Default);
            if (f is null) continue;

            _cache.Push(f.Key.ToString(), f.TimestampUtc, f.Value);

            // Optional debug:
            Console.WriteLine($"filtered {f.Key} {f.Value:F2} @ {f.TimestampUtc:HH:mm:ss}");
        }
    }, ct);
}

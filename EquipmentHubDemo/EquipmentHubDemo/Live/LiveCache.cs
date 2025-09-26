using EquipmentHubDemo.Domain;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

using static EquipmentHubDemo.Messaging.Zmq;

namespace EquipmentHubDemo.Live;

public interface ILiveCache
{
    event Action? Updated;

    IReadOnlyList<string> Keys { get; }

    IReadOnlyList<(DateTime X, double Y)> GetSeries(string key);

    void Push(string key, DateTime x, double y);
}

public sealed record LiveCacheOptions
{
    public const string SectionName = "LiveCache";

    public int MaxPointsPerKey { get; init; } = 2_000;
}

/// <summary>
/// In-memory cache of latest time series for the UI.
/// </summary>
public sealed class LiveCache : ILiveCache
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(DateTime X, double Y)>> _series = new();
    private readonly int _maxPointsPerKey;

    public LiveCache(IOptions<LiveCacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(options));

        if (value.MaxPointsPerKey <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), value.MaxPointsPerKey, "MaxPointsPerKey must be positive.");
        }

        _maxPointsPerKey = value.MaxPointsPerKey;
    }

    public event Action? Updated;

    public IReadOnlyList<string> Keys => _series.Keys.OrderBy(k => k).ToArray();

    public IReadOnlyList<(DateTime X, double Y)> GetSeries(string key)
        => _series.TryGetValue(key, out var q) ? q.ToArray() : Array.Empty<(DateTime, double)>();

    public void Push(string key, DateTime x, double y)
    {
        var q = _series.GetOrAdd(key, _ => new ConcurrentQueue<(DateTime, double)>());
        q.Enqueue((x, y));
        while (q.Count > _maxPointsPerKey && q.TryDequeue(out _)) { }
        Updated?.Invoke();
    }
}

public sealed class LiveSubscriberWorker : BackgroundService
{
    private readonly ILiveCache _cache;

    public LiveSubscriberWorker(ILiveCache cache) => _cache = cache;

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        AsyncIO.ForceDotNet.Force();

        using var sub = new SubscriberSocket();
        sub.Connect(SubConnect);
        sub.Subscribe(TopicFiltered);
        NetMQMessage? msg = null;

        while (!ct.IsCancellationRequested)
        {
            if (!sub.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref msg) || msg is null || msg.FrameCount < 2)
            {
                continue;
            }

            var json = msg[1].ConvertToString();

            // 👇 use shared options
            var f = JsonSerializer.Deserialize<FilteredMeasurement>(json, JsonOptions.Default);
            if (f is null) continue;

            _cache.Push(f.Key.ToString(), f.TimestampUtc, f.Value);

            // Optional debug:
            Console.WriteLine($"filtered {f.Key} {f.Value:F2} @ {f.TimestampUtc:HH:mm:ss}");
        }
        return Task.CompletedTask;
    }
}

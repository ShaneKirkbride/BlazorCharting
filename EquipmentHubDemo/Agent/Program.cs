// Agent/Program.cs
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetMQ;
using NetMQ.Sockets;

namespace Agent;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly string[] Instruments = { "UXG-01", "UXG-02" };
    private static readonly string[] Metrics = { "Power", "SNR" };

    private const string PubConnect = "tcp://127.0.0.1:5556"; // connects to XSUB side
    private const string TopicMeasure = "measure";

    public static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine("Agent: publishing to 'measure' (Ctrl+C to stop)…");

        // Required for NetMQ in .NET Core apps.
        AsyncIO.ForceDotNet.Force();

        try
        {
            using var pub = new PublisherSocket();
            pub.Connect(PubConnect);

            var rnd = new Random();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)); // ~10 Hz per metric

            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                var now = DateTime.UtcNow;
                double t = now.Ticks / 2e7; // slow varying angle

                foreach (var inst in Instruments)
                    foreach (var met in Metrics)
                    {
                        var key = new MeasureKey(inst, met);
                        double val = met switch
                        {
                            "Power" => 10 + 2 * Math.Sin(t) + rnd.NextDouble(),
                            "SNR" => 30 + 5 * Math.Cos(t) + rnd.NextDouble(),
                            _ => rnd.NextDouble()
                        };

                        var m = new Measurement(key, val, now);
                        var json = JsonSerializer.Serialize(m, JsonOpts);

                        // topic + payload
                        var msg = new NetMQMessage(2);
                        msg.Append(TopicMeasure);
                        msg.Append(json);
                        pub.SendMultipartMessage(msg);
                    }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            NetMQConfig.Cleanup();
            Console.WriteLine("Agent: stopped.");
        }
    }

    public readonly record struct MeasureKey(string InstrumentId, string Metric)
    {
        public override string ToString() => $"{InstrumentId}:{Metric}";
    }

    public sealed record Measurement(MeasureKey Key, double Value, DateTime TimestampUtc);
}

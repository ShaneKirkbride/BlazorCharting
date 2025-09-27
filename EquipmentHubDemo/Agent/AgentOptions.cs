using System.Collections.Generic;
using System.Linq;

namespace Agent;

public sealed class AgentOptions
{
    private static readonly TimeSpan DefaultPublishInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultRetryBackoff = TimeSpan.FromMilliseconds(100);

    public string PublishEndpoint { get; set; } = "tcp://127.0.0.1:5556";

    public string Topic { get; set; } = "measure";

    public int PublishIntervalMilliseconds { get; set; } = (int)DefaultPublishInterval.TotalMilliseconds;

    public int SendRetryCount { get; set; } = 3;

    public int SendRetryBackoffMilliseconds { get; set; } = (int)DefaultRetryBackoff.TotalMilliseconds;

    public List<InstrumentOptions> Instruments { get; set; } = CreateDefaultInstruments();

    public TimeSpan PublishInterval => TimeSpan.FromMilliseconds(PublishIntervalMilliseconds);

    public TimeSpan RetryBackoff => TimeSpan.FromMilliseconds(SendRetryBackoffMilliseconds);

    public void Normalize()
    {
        PublishEndpoint = string.IsNullOrWhiteSpace(PublishEndpoint)
            ? "tcp://127.0.0.1:5556"
            : PublishEndpoint.Trim();

        Topic = string.IsNullOrWhiteSpace(Topic)
            ? "measure"
            : Topic.Trim();

        if (PublishIntervalMilliseconds <= 0)
        {
            PublishIntervalMilliseconds = (int)DefaultPublishInterval.TotalMilliseconds;
        }

        if (SendRetryCount <= 0)
        {
            SendRetryCount = 3;
        }

        if (SendRetryBackoffMilliseconds < 0)
        {
            SendRetryBackoffMilliseconds = (int)DefaultRetryBackoff.TotalMilliseconds;
        }

        Instruments = (Instruments ?? new List<InstrumentOptions>())
            .Select(i => i?.Clone())
            .Where(i => i is not null)
            .Cast<InstrumentOptions>()
            .ToList();

        foreach (var instrument in Instruments.ToList())
        {
            instrument.Normalize();
            if (string.IsNullOrWhiteSpace(instrument.InstrumentId) || instrument.Metrics.Count == 0)
            {
                Instruments.Remove(instrument);
            }
        }

        if (Instruments.Count == 0)
        {
            Instruments = CreateDefaultInstruments();
        }
    }

    private static List<InstrumentOptions> CreateDefaultInstruments() =>
        new()
        {
            new InstrumentOptions
            {
                InstrumentId = "UXG-01",
                Metrics = new List<string> { "Temperature", "Humidity", "Power (240VAC)" }
            },
            new InstrumentOptions
            {
                InstrumentId = "UXG-02",
                Metrics = new List<string> { "Temperature", "Humidity", "Power (240VAC)" }
            }
        };
}

using System.Collections.Generic;

namespace Agent;

public sealed class AgentOptions
{
    private static readonly TimeSpan DefaultPublishInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultRetryBackoff = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromMilliseconds(250);
    private const int DefaultSendHighWatermark = 1000;

    public string PublishEndpoint { get; set; } = "tcp://127.0.0.1:5556";

    public string Topic { get; set; } = "measure";

    public int PublishIntervalMilliseconds { get; set; } = (int)DefaultPublishInterval.TotalMilliseconds;

    public int SendRetryCount { get; set; } = 3;

    public int SendRetryBackoffMilliseconds { get; set; } = (int)DefaultRetryBackoff.TotalMilliseconds;

    public int SendTimeoutMilliseconds { get; set; } = (int)DefaultSendTimeout.TotalMilliseconds;

    public int SendHighWatermark { get; set; } = DefaultSendHighWatermark;

    public InstrumentOptions Instrument { get; set; } = CreateDefaultInstrument();

    public TimeSpan PublishInterval => TimeSpan.FromMilliseconds(PublishIntervalMilliseconds);

    public TimeSpan RetryBackoff => TimeSpan.FromMilliseconds(SendRetryBackoffMilliseconds);

    public TimeSpan SendTimeout => TimeSpan.FromMilliseconds(SendTimeoutMilliseconds);

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

        if (SendTimeoutMilliseconds <= 0)
        {
            SendTimeoutMilliseconds = (int)DefaultSendTimeout.TotalMilliseconds;
        }

        if (SendHighWatermark <= 0)
        {
            SendHighWatermark = DefaultSendHighWatermark;
        }

        var instrument = Instrument?.Clone();
        instrument ??= CreateDefaultInstrument();
        instrument.Normalize();

        if (string.IsNullOrWhiteSpace(instrument.InstrumentId) || instrument.Metrics.Count == 0)
        {
            instrument = CreateDefaultInstrument();
            instrument.Normalize();
        }

        Instrument = instrument;
    }

    private static InstrumentOptions CreateDefaultInstrument() =>
        new()
        {
            InstrumentId = "UXG-01",
            Metrics = new List<string> { "Temperature", "Humidity", "Power (240VAC)" }
        };
}

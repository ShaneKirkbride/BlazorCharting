using System.Threading;

namespace EquipmentHubDemo.Tests;

internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan offset) => _utcNow += offset;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new NoopTimer();

    private sealed class NoopTimer : ITimer
    {
        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
    }
}

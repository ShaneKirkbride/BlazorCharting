using System;
using EquipmentHubDemo.Live;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Tests.Live;

public sealed class LiveCacheTests
{
    [Fact]
    public void Constructor_ThrowsWhenOptionsInvalid()
    {
        var options = Options.Create(new LiveCacheOptions { MaxPointsPerKey = 0 });

        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveCache(options));
    }

    [Fact]
    public void Push_NotifiesSubscribers()
    {
        var options = Options.Create(new LiveCacheOptions { MaxPointsPerKey = 10 });
        var cache = new LiveCache(options);
        var notified = false;
        cache.Updated += () => notified = true;

        cache.Push("A:Temperature", DateTime.UtcNow, 25.2);

        Assert.True(notified);
    }

    [Fact]
    public void Keys_ReturnedInSortedOrder()
    {
        var options = Options.Create(new LiveCacheOptions { MaxPointsPerKey = 10 });
        var cache = new LiveCache(options);
        cache.Push("B:Humidity", DateTime.UtcNow, 40);
        cache.Push("A:Temperature", DateTime.UtcNow, 22);
        cache.Push("C:Power", DateTime.UtcNow, 100);

        var keys = cache.Keys;

        Assert.Equal(new[] { "A:Temperature", "B:Humidity", "C:Power" }, keys);
    }

    [Fact]
    public void Push_EnforcesCapacityPerKey()
    {
        var options = Options.Create(new LiveCacheOptions { MaxPointsPerKey = 3 });
        var cache = new LiveCache(options);
        var key = "A:Temperature";

        cache.Push(key, new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc), 20);
        cache.Push(key, new DateTime(2024, 01, 01, 0, 1, 0, DateTimeKind.Utc), 21);
        cache.Push(key, new DateTime(2024, 01, 01, 0, 2, 0, DateTimeKind.Utc), 22);
        cache.Push(key, new DateTime(2024, 01, 01, 0, 3, 0, DateTimeKind.Utc), 23);

        var series = cache.GetSeries(key);

        Assert.Equal(3, series.Count);
        Assert.Equal(21, series[0].Y);
        Assert.Equal(22, series[1].Y);
        Assert.Equal(23, series[2].Y);
    }
}

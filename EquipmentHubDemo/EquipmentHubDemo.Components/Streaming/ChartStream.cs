using System;
using System.Collections.Generic;
using EquipmentHubDemo.Domain;

namespace EquipmentHubDemo.Components.Streaming;

internal sealed class ChartStream
{
    private const int MaxPoints = 2000;

    public ChartStream(string key)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public string Key { get; }

    public List<PointDto> Points { get; } = new();

    public long SinceTicks { get; private set; }

    public long TotalReceived { get; private set; }

    public bool Apply(IReadOnlyList<PointDto> batch)
    {
        if (batch is null)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        var newPoints = 0;
        foreach (var point in batch)
        {
            if (point is null)
            {
                throw new ArgumentException("Batch cannot contain null points.", nameof(batch));
            }

            Points.Add(point);
            SinceTicks = Math.Max(SinceTicks, point.X.Ticks);
            newPoints++;
        }

        if (Points.Count > MaxPoints)
        {
            var excess = Points.Count - MaxPoints;
            Points.RemoveRange(0, excess);
        }

        if (newPoints == 0)
        {
            return false;
        }

        TotalReceived += newPoints;
        return true;
    }

    public void Reset()
    {
        Points.Clear();
        SinceTicks = 0;
        TotalReceived = 0;
    }
}

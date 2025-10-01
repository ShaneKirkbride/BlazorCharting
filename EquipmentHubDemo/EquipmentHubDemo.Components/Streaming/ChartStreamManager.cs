using System;
using System.Collections.Generic;
using System.Linq;

namespace EquipmentHubDemo.Components.Streaming;

internal sealed class ChartStreamManager
{
    private readonly Dictionary<string, ChartStream> _streams = new(StringComparer.Ordinal);

    public long TotalReceived => _streams.Values.Sum(stream => stream.TotalReceived);

    public int CountActivePoints(IReadOnlyCollection<string> selectedKeys)
    {
        if (selectedKeys.Count == 0 || _streams.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var key in selectedKeys)
        {
            if (_streams.TryGetValue(key, out var stream))
            {
                total += stream.Points.Count;
            }
        }

        return total;
    }

    public IEnumerable<ChartStream> GetActiveStreams(IReadOnlyCollection<string> selectedKeys)
    {
        foreach (var key in selectedKeys)
        {
            if (_streams.TryGetValue(key, out var stream))
            {
                yield return stream;
            }
        }
    }

    public bool TryGetStream(string key, out ChartStream stream)
    {
        if (_streams.TryGetValue(key, out var existing))
        {
            stream = existing;
            return true;
        }

        stream = null!;
        return false;
    }

    public void ResetForSelection(IReadOnlyCollection<string> selectedKeys)
    {
        foreach (var key in selectedKeys)
        {
            EnsureStream(key).Reset();
        }

        PruneStreams(selectedKeys);
    }

    public void EnsureSelection(IReadOnlyCollection<string> selectedKeys)
    {
        foreach (var key in selectedKeys)
        {
            EnsureStream(key);
        }

        PruneStreams(selectedKeys);
    }

    public void Clear()
        => _streams.Clear();

    private ChartStream EnsureStream(string key)
    {
        if (!_streams.TryGetValue(key, out var stream))
        {
            stream = new ChartStream(key);
            _streams[key] = stream;
        }

        return stream;
    }

    private void PruneStreams(IReadOnlyCollection<string> selectedKeys)
    {
        if (_streams.Count == 0)
        {
            return;
        }

        var activeKeys = new HashSet<string>(selectedKeys, StringComparer.Ordinal);
        var keysToRemove = _streams.Keys
            .Where(key => !activeKeys.Contains(key))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _streams.Remove(key);
        }
    }
}

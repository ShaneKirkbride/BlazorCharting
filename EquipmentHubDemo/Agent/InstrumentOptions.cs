using System.Collections.Generic;
using System.Linq;

namespace Agent;

public sealed class InstrumentOptions
{
    public string InstrumentId { get; set; } = string.Empty;

    public List<string> Metrics { get; set; } = new();

    internal InstrumentOptions Clone() => new()
    {
        InstrumentId = InstrumentId,
        Metrics = (Metrics ?? new List<string>()).ToList()
    };

    internal void Normalize()
    {
        InstrumentId = InstrumentId?.Trim() ?? string.Empty;
        var metrics = Metrics ?? new List<string>();
        Metrics = metrics
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

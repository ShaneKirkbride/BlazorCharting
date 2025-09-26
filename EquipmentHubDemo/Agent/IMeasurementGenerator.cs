using System.Collections.Generic;

namespace Agent;

internal interface IMeasurementGenerator
{
    IEnumerable<Measurement> CreateMeasurements(DateTime timestampUtc);
}

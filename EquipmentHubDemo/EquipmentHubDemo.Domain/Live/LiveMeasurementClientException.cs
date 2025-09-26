namespace EquipmentHubDemo.Domain.Live;

/// <summary>
/// Represents errors that occur when retrieving live measurements from an API endpoint.
/// </summary>
public sealed class LiveMeasurementClientException : Exception
{
    public LiveMeasurementClientException(string message)
        : base(message)
    {
    }

    public LiveMeasurementClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

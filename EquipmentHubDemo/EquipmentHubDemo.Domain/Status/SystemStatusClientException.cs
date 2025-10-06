namespace EquipmentHubDemo.Domain.Status;

public sealed class SystemStatusClientException : Exception
{
    public SystemStatusClientException(string message)
        : base(message)
    {
    }

    public SystemStatusClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

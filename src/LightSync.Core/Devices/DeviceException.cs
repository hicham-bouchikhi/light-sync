namespace LightSync.Core.Devices;

/// <summary>
/// A device-layer failure that is safe to show the user. Adapters must never place secrets
/// such as API tokens in the message.
/// </summary>
public class DeviceException : Exception
{
    public DeviceException()
    {
    }

    public DeviceException(string message)
        : base(message)
    {
    }

    public DeviceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DeviceAuthenticationException : DeviceException
{
    public DeviceAuthenticationException()
    {
    }

    public DeviceAuthenticationException(string message)
        : base(message)
    {
    }

    public DeviceAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DeviceUnreachableException : DeviceException
{
    public DeviceUnreachableException()
    {
    }

    public DeviceUnreachableException(string message)
        : base(message)
    {
    }

    public DeviceUnreachableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

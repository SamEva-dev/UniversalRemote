namespace UniversalRemote.Remote.Abstractions;

public enum RemoteErrorCode
{
    None, DeviceNotFound, UnsupportedAction, ProviderUnavailable, PairingRequired,
    Timeout, TransportFailure, ProviderFailure
}

/// <summary>Accepted is not proof that the physical device changed state, especially for IR.</summary>
public enum DeliveryState { NotSent, Accepted, Unknown }

/// <summary>No raw exception text, endpoints or secrets cross the public error boundary.</summary>
public sealed record RemoteResult
{
    public RemoteErrorCode Error { get; }
    public DeliveryState Delivery { get; }
    public bool IsSuccess => Error == RemoteErrorCode.None;
    private RemoteResult(RemoteErrorCode error, DeliveryState delivery)
    {
        Error = error;
        Delivery = delivery;
    }
    public static RemoteResult Accepted() => new(RemoteErrorCode.None, DeliveryState.Accepted);
    public static RemoteResult Failed(RemoteErrorCode error, DeliveryState delivery = DeliveryState.NotSent)
    {
        if (error == RemoteErrorCode.None || !Enum.IsDefined(error)) throw new ArgumentOutOfRangeException(nameof(error));
        if (delivery is not (DeliveryState.NotSent or DeliveryState.Unknown)) throw new ArgumentOutOfRangeException(nameof(delivery));
        return new(error, delivery);
    }
}

public interface IDeviceRepository
{
    Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Implementations must honor cancellation and return sanitized results. Never retry implicitly.</summary>
public interface IRemoteProvider
{
    string Id { get; }
    SupportLevel SupportLevel { get; }
    Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken);
}

public interface IRemoteControl
{
    Task<RemoteResult> ExecuteAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default);
}

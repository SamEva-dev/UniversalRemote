namespace UniversalRemote.Remote.Abstractions;

/// <summary>A persisted shortcut to a supported action of one device.</summary>
public sealed record Favorite
{
    public Guid DeviceId { get; }
    public RemoteAction Action { get; }
    public int Position { get; }

    public Favorite(Guid deviceId, RemoteAction action, int position)
    {
        if (deviceId == Guid.Empty) throw new ArgumentException("Favorite device ID must not be empty.", nameof(deviceId));
        ArgumentNullException.ThrowIfNull(action);
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position), "Favorite position must be zero or greater.");

        DeviceId = deviceId;
        Action = action;
        Position = position;
    }
}

/// <summary>Device-scoped local favorites. Credentials and secrets never belong in this store.</summary>
public interface IFavoriteRepository
{
    Task<IReadOnlyList<Favorite>> ListAsync(Guid deviceId, CancellationToken cancellationToken = default);
    Task<Favorite> AddAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default);
}

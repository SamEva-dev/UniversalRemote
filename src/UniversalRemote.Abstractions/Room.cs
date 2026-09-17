namespace UniversalRemote.Remote.Abstractions;

/// <summary>Logical grouping of devices. A device may belong to at most one Room at a time.</summary>
public sealed class Room
{
    public Guid Id { get; }
    public string Name { get; }
    public IReadOnlyList<Guid> DeviceIds { get; }

    public Room(Guid id, string name, IEnumerable<Guid>? deviceIds = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Room ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedName = name.Trim();
        if (normalizedName.Length > 60) throw new ArgumentOutOfRangeException(nameof(name), "Room name must be 60 characters or fewer.");

        var ids = (deviceIds ?? Array.Empty<Guid>()).ToArray();
        if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Room device IDs must not be empty.", nameof(deviceIds));
        if (ids.Distinct().Count() != ids.Length) throw new ArgumentException("Duplicate device in room.", nameof(deviceIds));

        Id = id;
        Name = normalizedName;
        DeviceIds = Array.AsReadOnly(ids);
    }
}

/// <summary>Persistence contract for logical rooms. Assigning a device moves it from any previous room.</summary>
public interface IRoomRepository
{
    Task<Room?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken = default);
    Task<Room> CreateAsync(string name, CancellationToken cancellationToken = default);
    Task<Room> RenameAsync(Guid roomId, string name, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<Room> AssignDeviceAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default);
    Task<Room> UnassignDeviceAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default);
}

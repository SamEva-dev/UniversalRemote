using UniversalRemote.Abstractions;

namespace UniversalRemote.Core;

/// <summary>Process-local room store for applications that do not opt into SQLite.</summary>
public sealed class InMemoryRoomRepository : IRoomRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Room> rooms = [];

    public Task<Room?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(rooms.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult<IReadOnlyList<Room>>(rooms.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id).ToArray());
    }

    public Task<Room> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var room = new Room(Guid.NewGuid(), name);
        lock (gate)
        {
            EnsureUniqueName(room);
            rooms.Add(room.Id, room);
            return Task.FromResult(room);
        }
    }

    public Task<Room> RenameAsync(Guid roomId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var room = new Room(roomId, name, GetRoom(roomId).DeviceIds);
            EnsureUniqueName(room);
            rooms[roomId] = room;
            return Task.FromResult(room);
        }
    }

    public Task<bool> DeleteAsync(Guid roomId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(rooms.Remove(roomId));
    }

    public Task<Room> AssignDeviceAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var current = GetRoom(roomId);
            var room = new Room(roomId, current.Name, current.DeviceIds.Append(deviceId).Distinct());
            foreach (var previous in rooms.Values.Where(r => r.Id != roomId && r.DeviceIds.Contains(deviceId)).ToArray())
                rooms[previous.Id] = new Room(previous.Id, previous.Name, previous.DeviceIds.Where(id => id != deviceId));
            rooms[roomId] = room;
            return Task.FromResult(room);
        }
    }

    public Task<Room> UnassignDeviceAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var current = GetRoom(roomId);
            var room = new Room(roomId, current.Name, current.DeviceIds.Where(id => id != deviceId));
            rooms[roomId] = room;
            return Task.FromResult(room);
        }
    }

    private Room GetRoom(Guid id) => rooms.GetValueOrDefault(id) ?? throw new KeyNotFoundException("Room not found.");

    private void EnsureUniqueName(Room room)
    {
        if (rooms.Values.Any(r => r.Id != room.Id && string.Equals(r.Name, room.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A room with this name already exists.");
    }
}

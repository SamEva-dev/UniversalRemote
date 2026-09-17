using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Persistence.Sqlite;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class RoomsTests
{
    [Fact]
    public void Room_rejects_duplicate_devices()
    {
        var deviceId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new Room(Guid.NewGuid(), "Salon", [deviceId, deviceId]));
    }

    [Fact]
    public async Task Sqlite_room_survives_repository_restart()
    {
        await using var database = new TemporaryDatabase();
        var room = await database.CreateRoomRepository().CreateAsync("Salon");

        var restored = await database.CreateRoomRepository().FindAsync(room.Id);

        Assert.NotNull(restored);
        Assert.Equal("Salon", restored.Name);
    }

    [Fact]
    public async Task Assigning_device_to_another_room_moves_it_atomically()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync("TV");
        var rooms = database.CreateRoomRepository();
        var salon = await rooms.CreateAsync("Salon");
        var chambre = await rooms.CreateAsync("Chambre");

        await rooms.AssignDeviceAsync(salon.Id, device.Id);
        var moved = await rooms.AssignDeviceAsync(chambre.Id, device.Id);

        Assert.Empty((await rooms.FindAsync(salon.Id))!.DeviceIds);
        Assert.Single(moved.DeviceIds);
        Assert.Equal(device.Id, moved.DeviceIds[0]);
    }

    [Fact]
    public async Task Deleting_room_keeps_device_catalogue_entry()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync("Box");
        var rooms = database.CreateRoomRepository();
        var room = await rooms.CreateAsync("Salon");
        await rooms.AssignDeviceAsync(room.Id, device.Id);

        Assert.True(await rooms.DeleteAsync(room.Id));
        Assert.NotNull(await database.CreateDeviceRepository().FindAsync(device.Id));
    }

    [Fact]
    public async Task List_rooms_handler_projects_assigned_device_names()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync("TV du salon");
        var rooms = database.CreateRoomRepository();
        var room = await rooms.CreateAsync("Salon");
        await rooms.AssignDeviceAsync(room.Id, device.Id);
        var handler = new ListRoomsHandler(rooms, database.CreateDeviceRepository());

        var result = await handler.Handle(new ListRooms(), CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("Salon", summary.Name);
        Assert.Equal("TV du salon", Assert.Single(summary.Devices).DisplayName);
    }

    [Fact]
    public async Task Duplicate_room_names_are_rejected_case_insensitively()
    {
        await using var database = new TemporaryDatabase();
        var rooms = database.CreateRoomRepository();
        await rooms.CreateAsync("Salon");

        await Assert.ThrowsAsync<InvalidOperationException>(() => rooms.CreateAsync(" salon "));
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "UniversalRemote.RoomTests", Guid.NewGuid().ToString("N"));
        private readonly SqliteDeviceStoreOptions options;

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(directory);
            options = new SqliteDeviceStoreOptions(Path.Combine(directory, "rooms.db"));
        }

        public SqliteDeviceRepository CreateDeviceRepository() => new(options);
        public SqliteRoomRepository CreateRoomRepository() => new(options);

        public async Task<Device> AddDeviceAsync(string displayName)
        {
            var device = new Device(Guid.NewGuid(), displayName,
                [new DeviceRoute("room.test", Guid.NewGuid().ToString("N"), [RemoteActions.PowerToggle])]);
            await CreateDeviceRepository().UpsertAsync(device);
            return device;
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            return ValueTask.CompletedTask;
        }
    }
}

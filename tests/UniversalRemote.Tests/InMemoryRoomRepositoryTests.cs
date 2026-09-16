using UniversalRemote.Core;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class InMemoryRoomRepositoryTests
{
    [Fact]
    public async Task Assigning_device_moves_it_without_mutating_previous_snapshots()
    {
        var repository = new InMemoryRoomRepository();
        var first = await repository.CreateAsync("Living room");
        var second = await repository.CreateAsync("Bedroom");
        var deviceId = Guid.NewGuid();
        var snapshot = await repository.AssignDeviceAsync(first.Id, deviceId);

        await repository.AssignDeviceAsync(second.Id, deviceId);
        await repository.AssignDeviceAsync(second.Id, deviceId);

        Assert.Empty((await repository.FindAsync(first.Id))!.DeviceIds);
        Assert.Equal(deviceId, Assert.Single((await repository.FindAsync(second.Id))!.DeviceIds));
        Assert.Equal(deviceId, Assert.Single(snapshot.DeviceIds));
        Assert.Empty((await repository.UnassignDeviceAsync(second.Id, deviceId)).DeviceIds);
    }

    [Fact]
    public async Task Names_are_normalized_and_duplicate_rename_preserves_existing_room()
    {
        var repository = new InMemoryRoomRepository();
        var first = await repository.CreateAsync(" Living room ");
        var second = await repository.CreateAsync("Bedroom");
        Assert.Equal("Living room", first.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RenameAsync(second.Id, "LIVING ROOM"));
        Assert.Equal("Bedroom", (await repository.FindAsync(second.Id))!.Name);
        Assert.True(await repository.DeleteAsync(first.Id));
        Assert.Null(await repository.FindAsync(first.Id));
    }
}

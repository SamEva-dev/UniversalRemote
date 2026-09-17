using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Persistence.Sqlite;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class ActivitiesTests
{
    [Fact]
    public void Activity_requires_contiguous_ordered_positions()
    {
        var deviceId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new Activity(Guid.NewGuid(), "Film", steps:
        [
            new RemoteActionActivityStep(0, deviceId, RemoteActions.PowerToggle),
            new DelayActivityStep(2, TimeSpan.FromSeconds(1))
        ]));
    }

    [Theory]
    [InlineData(49)]
    [InlineData(60001)]
    public void Delay_step_rejects_out_of_range_duration(int milliseconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DelayActivityStep(0, TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public async Task Sqlite_activity_survives_restart_and_preserves_step_types_and_order()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle, RemoteActions.VolumeUp);
        var activity = new Activity(Guid.NewGuid(), "Film", steps:
        [
            new RemoteActionActivityStep(0, device.Id, RemoteActions.PowerToggle),
            new DelayActivityStep(1, TimeSpan.FromMilliseconds(750)),
            new RemoteActionActivityStep(2, device.Id, RemoteActions.VolumeUp)
        ]);
        await database.CreateActivityRepository().SaveAsync(activity);

        var restored = await database.CreateActivityRepository().FindAsync(activity.Id);

        Assert.NotNull(restored);
        Assert.Equal("Film", restored!.Name);
        Assert.IsType<RemoteActionActivityStep>(restored.Steps[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(750), ((DelayActivityStep)restored.Steps[1]).Duration);
        Assert.Equal(RemoteActions.VolumeUp, ((RemoteActionActivityStep)restored.Steps[2]).Action);
    }

    [Fact]
    public async Task Save_handler_rejects_action_not_supported_by_device()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle);
        var repository = database.CreateActivityRepository();
        var activity = await repository.SaveAsync(new Activity(Guid.NewGuid(), "Unsupported action"));
        var handler = new SaveActivityHandler(repository, database.CreateDeviceRepository(), database.CreateRoomRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new SaveActivity(activity.Id, activity.Name, null,
                [new ActivityStepInput(0, ActivityStepInputKind.RemoteAction, device.Id, RemoteActions.VolumeUp.Id)]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_handler_rejects_unknown_room()
    {
        await using var database = new TemporaryDatabase();
        var handler = new CreateActivityHandler(database.CreateActivityRepository(), database.CreateRoomRepository());
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new CreateActivity("Film", Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_room_keeps_activity_and_clears_optional_room_reference()
    {
        await using var database = new TemporaryDatabase();
        var rooms = database.CreateRoomRepository();
        var room = await rooms.CreateAsync("Salon");
        var activities = database.CreateActivityRepository();
        var activity = await activities.SaveAsync(new Activity(Guid.NewGuid(), "Film", room.Id));

        Assert.True(await rooms.DeleteAsync(room.Id));
        var restored = await activities.FindAsync(activity.Id);

        Assert.NotNull(restored);
        Assert.Null(restored!.RoomId);
    }

    [Fact]
    public async Task Saving_activity_replaces_previous_steps_atomically()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle, RemoteActions.VolumeUp);
        var activities = database.CreateActivityRepository();
        var id = Guid.NewGuid();
        await activities.SaveAsync(new Activity(id, "Film", steps:
            [new RemoteActionActivityStep(0, device.Id, RemoteActions.PowerToggle)]));

        await activities.SaveAsync(new Activity(id, "Film du soir", steps:
        [
            new DelayActivityStep(0, TimeSpan.FromMilliseconds(500)),
            new RemoteActionActivityStep(1, device.Id, RemoteActions.VolumeUp)
        ]));

        var restored = await activities.FindAsync(id);
        Assert.NotNull(restored);
        Assert.Equal("Film du soir", restored!.Name);
        Assert.Equal(2, restored.Steps.Count);
        Assert.IsType<DelayActivityStep>(restored.Steps[0]);
    }

    [Fact]
    public async Task Delete_activity_cascades_steps()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle);
        var activities = database.CreateActivityRepository();
        var activity = await activities.SaveAsync(new Activity(Guid.NewGuid(), "Temporary", steps:
            [new RemoteActionActivityStep(0, device.Id, RemoteActions.PowerToggle)]));

        Assert.True(await activities.DeleteAsync(activity.Id));
        Assert.Null(await activities.FindAsync(activity.Id));
    }

    [Fact]
    public async Task Save_handler_persists_supported_actions_and_delays()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle);
        var activities = database.CreateActivityRepository();
        var created = await activities.SaveAsync(new Activity(Guid.NewGuid(), "Film"));
        var handler = new SaveActivityHandler(activities, database.CreateDeviceRepository(), database.CreateRoomRepository());

        var summary = await handler.Handle(new SaveActivity(created.Id, "Film", null,
        [
            new ActivityStepInput(0, ActivityStepInputKind.RemoteAction, device.Id, RemoteActions.PowerToggle.Id),
            new ActivityStepInput(1, ActivityStepInputKind.Delay, DelayMilliseconds: 1200)
        ]), CancellationToken.None);

        Assert.Equal(2, summary.Steps.Count);
        Assert.Equal(ActivityStepInputKind.RemoteAction, summary.Steps[0].Kind);
        Assert.Equal(1200, summary.Steps[1].DelayMilliseconds!.Value);
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "UniversalRemote.ActivityTests", Guid.NewGuid().ToString("N"));
        private readonly SqliteDeviceStoreOptions options;

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(directory);
            options = new SqliteDeviceStoreOptions(Path.Combine(directory, "activities.db"));
        }

        public SqliteDeviceRepository CreateDeviceRepository() => new(options);
        public SqliteRoomRepository CreateRoomRepository() => new(options);
        public SqliteActivityRepository CreateActivityRepository() => new(options);

        public async Task<Device> AddDeviceAsync(params RemoteAction[] capabilities)
        {
            var device = new Device(Guid.NewGuid(), "Activity test device",
                [new DeviceRoute("activity.test", Guid.NewGuid().ToString("N"), capabilities)]);
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

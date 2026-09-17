using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Persistence.Sqlite;
using UniversalRemote.Remote.Presentation;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class FavoritesTests
{
    [Fact]
    public void Favorite_rejects_negative_position()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Favorite(Guid.NewGuid(), RemoteActions.PowerToggle, -1));
    }

    [Fact]
    public async Task Sqlite_favorites_survive_restart_and_preserve_insertion_order()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle, RemoteActions.VolumeUp);
        var firstRepository = database.CreateFavoriteRepository();
        await firstRepository.AddAsync(device.Id, RemoteActions.VolumeUp);
        await firstRepository.AddAsync(device.Id, RemoteActions.PowerToggle);

        var restored = await database.CreateFavoriteRepository().ListAsync(device.Id);

        Assert.Equal(new[] { RemoteActions.VolumeUp.Id, RemoteActions.PowerToggle.Id }, restored.Select(x => x.Action.Id));
        Assert.Equal(new[] { 0, 1 }, restored.Select(x => x.Position));
    }

    [Fact]
    public async Task Adding_same_favorite_twice_is_idempotent()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle);
        var favorites = database.CreateFavoriteRepository();

        var first = await favorites.AddAsync(device.Id, RemoteActions.PowerToggle);
        var second = await favorites.AddAsync(device.Id, RemoteActions.PowerToggle);

        Assert.Equal(first, second);
        Assert.Single(await favorites.ListAsync(device.Id));
    }

    [Fact]
    public async Task Add_handler_rejects_action_not_supported_by_device()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle);
        var handler = new AddDeviceFavoriteHandler(database.CreateFavoriteRepository(), database.CreateDeviceRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new AddDeviceFavorite(device.Id, RemoteActions.VolumeUp.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Removing_favorite_persists()
    {
        await using var database = new TemporaryDatabase();
        var device = await database.AddDeviceAsync(RemoteActions.PowerToggle);
        var favorites = database.CreateFavoriteRepository();
        await favorites.AddAsync(device.Id, RemoteActions.PowerToggle);

        Assert.True(await favorites.RemoveAsync(device.Id, RemoteActions.PowerToggle));
        Assert.Empty(await database.CreateFavoriteRepository().ListAsync(device.Id));
    }

    [Fact]
    public void Ui_model_exposes_favorites_without_duplicating_capability_controls()
    {
        var device = new Device(Guid.NewGuid(), "TV", [new DeviceRoute("test", "key", [RemoteActions.PowerToggle, RemoteActions.VolumeUp])]);

        var model = RemoteUiFavoriteProjection.Apply(new CapabilityRemoteUiModelBuilder().Build(device), [RemoteActions.VolumeUp.Id]);

        Assert.Equal(2, model.Controls.Count);
        var favorite = Assert.Single(model.Favorites);
        Assert.Equal(RemoteActions.VolumeUp, favorite.Action);
        Assert.Same(model.Controls.Single(x => x.Action == RemoteActions.VolumeUp), favorite);
    }

    [Fact]
    public void Ui_model_ignores_stale_or_unknown_favorite_ids()
    {
        var device = new Device(Guid.NewGuid(), "TV", [new DeviceRoute("test", "key", [RemoteActions.PowerToggle])]);

        var model = RemoteUiFavoriteProjection.Apply(new CapabilityRemoteUiModelBuilder().Build(device), ["future.unsupported", RemoteActions.PowerToggle.Id]);

        Assert.Equal(RemoteActions.PowerToggle, Assert.Single(model.Favorites).Action);
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "UniversalRemote.FavoriteTests", Guid.NewGuid().ToString("N"));
        private readonly SqliteDeviceStoreOptions options;

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(directory);
            options = new SqliteDeviceStoreOptions(Path.Combine(directory, "favorites.db"));
        }

        public SqliteDeviceRepository CreateDeviceRepository() => new(options);
        public SqliteFavoriteRepository CreateFavoriteRepository() => new(options);

        public async Task<Device> AddDeviceAsync(params RemoteAction[] capabilities)
        {
            var device = new Device(Guid.NewGuid(), "Favorite test device",
                [new DeviceRoute("favorite.test", Guid.NewGuid().ToString("N"), capabilities)]);
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

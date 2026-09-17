using Xunit;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Core;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Persistence.Sqlite;

namespace UniversalRemote.Remote.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task InMemory_pairing_preserves_device_id_for_same_route()
    {
        var repository = new InMemoryDeviceRepository();
        var first = await repository.RegisterPairingAsync("Salon", Route("provider.test", "device-1", RemoteActions.PowerToggle));
        var second = await repository.RegisterPairingAsync("Salon TV", Route("provider.test", "device-1", RemoteActions.PowerToggle, RemoteActions.VolumeUp));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Salon TV", second.DisplayName);
        Assert.Contains(RemoteActions.VolumeUp, second.Capabilities);
    }

    [Fact]
    public async Task Sqlite_pairing_preserves_device_id_across_repository_instances()
    {
        await using var database = new TemporaryDatabase();
        var firstRepository = database.CreateRepository();
        var first = await firstRepository.RegisterPairingAsync("TV", Route("provider.test", "stable-key", RemoteActions.PowerToggle));

        var secondRepository = database.CreateRepository();
        var second = await secondRepository.RegisterPairingAsync("TV renommée", Route("provider.test", "stable-key", RemoteActions.PowerToggle, RemoteActions.VolumeDown));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("TV renommée", second.DisplayName);
        Assert.Contains(RemoteActions.VolumeDown, second.Capabilities);
    }

    [Fact]
    public async Task Sqlite_repository_survives_restart_and_preserves_route_order()
    {
        await using var database = new TemporaryDatabase();
        var id = Guid.NewGuid();
        var expected = new Device(id, "Cinéma", [
            Route("provider.a", "a-1", RemoteActions.PowerToggle, RemoteActions.Home),
            Route("provider.b", "b-1", RemoteActions.VolumeUp, RemoteActions.VolumeDown)
        ]);
        await database.CreateRepository().UpsertAsync(expected);

        var restored = await database.CreateRepository().FindAsync(id);

        Assert.NotNull(restored);
        Assert.Equal(expected.Id, restored.Id);
        Assert.Equal(expected.DisplayName, restored.DisplayName);
        Assert.Equal(new[] { "provider.a", "provider.b" }, restored.Routes.Select(x => x.ProviderId));
        Assert.Equal(new[] { "a-1", "b-1" }, restored.Routes.Select(x => x.DeviceKey));
        Assert.Equal(expected.Capabilities.OrderBy(x => x.Id), restored.Capabilities.OrderBy(x => x.Id));
    }

    [Fact]
    public async Task Sqlite_different_route_creates_different_device()
    {
        await using var database = new TemporaryDatabase();
        var repository = database.CreateRepository();
        var first = await repository.RegisterPairingAsync("TV 1", Route("provider.test", "key-1", RemoteActions.PowerToggle));
        var second = await repository.RegisterPairingAsync("TV 2", Route("provider.test", "key-2", RemoteActions.PowerToggle));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, (await repository.ListAsync()).Count);
    }

    [Fact]
    public async Task Sqlite_rejects_route_owned_by_another_device()
    {
        await using var database = new TemporaryDatabase();
        var repository = database.CreateRepository();
        var route = Route("provider.test", "unique-route", RemoteActions.PowerToggle);
        await repository.UpsertAsync(new Device(Guid.NewGuid(), "A", [route]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpsertAsync(new Device(Guid.NewGuid(), "B", [route])));
    }


    [Fact]
    public async Task Complete_pairing_handler_reuses_registrar_identity()
    {
        var repository = new InMemoryDeviceRepository();
        var provider = new StaticPairingProvider(Route("provider.pairing", "device-stable", RemoteActions.PowerToggle));
        var handler = new CompletePairingHandler([provider], repository);

        var first = await handler.Handle(new CompletePairing(provider.Id, Guid.NewGuid(), string.Empty, "TV"), CancellationToken.None);
        var second = await handler.Handle(new CompletePairing(provider.Id, Guid.NewGuid(), string.Empty, "TV"), CancellationToken.None);

        Assert.Equal(first.DeviceId, second.DeviceId);
    }

    private static DeviceRoute Route(string providerId, string deviceKey, params RemoteAction[] capabilities)
        => new(providerId, deviceKey, capabilities);


    private sealed class StaticPairingProvider(DeviceRoute route) : IDevicePairingProvider
    {
        public string Id => route.ProviderId;
        public PairingCandidate? Match(PairingProbe probe) => null;
        public Task<PairingChallenge> StartAsync(PairingCandidate candidate, CancellationToken cancellationToken)
            => Task.FromResult(new PairingChallenge(Guid.NewGuid(), Id, route.DeviceKey, candidate.DisplayName, string.Empty, 0));
        public Task<PairingCompletion> CompleteAsync(Guid challengeId, string code, CancellationToken cancellationToken)
            => Task.FromResult(new PairingCompletion(route, "TV"));
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "UniversalRemote.Tests", Guid.NewGuid().ToString("N"));
        public TemporaryDatabase() => Directory.CreateDirectory(directory);
        public SqliteDeviceRepository CreateRepository()
            => new(new SqliteDeviceStoreOptions(Path.Combine(directory, "devices.db")));
        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            return ValueTask.CompletedTask;
        }
    }
}

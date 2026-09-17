using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Provider.Freebox;
using UniversalRemote.Remote.Provider.GenericUpnp;
using Xunit;
namespace UniversalRemote.Remote.Tests;

public sealed class Sprint6ProviderTests
{
    [Fact]
    public void Freebox_pairing_matches_fbx_api_service()
    {
        var store = new MemoryFreeboxStore();
        var provider = new FreeboxPairingProvider(store);
        var match = provider.Match(new PairingProbe("Freebox Server", ["192.168.1.254"], ["_fbx-api._tcp.local"]));
        Assert.NotNull(match);
        Assert.Equal(FreeboxRemoteProvider.ProviderId, match!.ProviderId);
    }

    [Fact]
    public void Generic_upnp_route_exposes_only_verified_rendering_capabilities()
    {
        var route = GenericUpnpRemoteProvider.CreateRoute("http://192.168.1.20/device.xml");
        Assert.Equal(GenericUpnpRemoteProvider.ProviderId, route.ProviderId);
        Assert.Equal(3, route.Capabilities.Count);
    }

    private sealed class MemoryFreeboxStore : IFreeboxRemoteCodeStore
    {
        public Task<string?> GetAsync(string deviceKey, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task SaveAsync(string deviceKey, string code, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

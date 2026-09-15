using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Core;
using UniversalRemote.Provider.AndroidTv;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class AndroidTvTests
{
    [Fact]
    public void Discovery_matcher_accepts_android_tv_remote_service()
    {
        var provider = new AndroidTvPairingProvider(new InMemoryAndroidTvCredentialStore());
        var candidate = provider.Match(new PairingProbe("Living TV", ["192.168.1.42"], ["_androidtvremote2._tcp.local"]));
        Assert.NotNull(candidate);
        Assert.Equal(AndroidTvPairingProvider.ProviderId, candidate!.ProviderId);
        Assert.Equal("192.168.1.42", candidate.DeviceKey);
    }

    [Fact]
    public async Task Android_tv_provider_requires_pairing_before_sending()
    {
        var provider = new AndroidTvRemoteProvider(new InMemoryAndroidTvCredentialStore());
        var route = new DeviceRoute(AndroidTvRemoteProvider.ProviderId, "192.168.1.42", AndroidTvRemoteProvider.Capabilities);
        var result = await provider.ExecuteAsync(route, RemoteActions.Home, CancellationToken.None);
        Assert.Equal(RemoteErrorCode.PairingRequired, result.Error);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task In_memory_registry_can_receive_a_paired_device()
    {
        var registry = new InMemoryDeviceRepository();
        var device = new Device(Guid.NewGuid(), "TV", [new DeviceRoute("androidtv", "host", [RemoteActions.Home])]);
        await registry.UpsertAsync(device);
        Assert.Equal(device, await registry.FindAsync(device.Id));
    }
}

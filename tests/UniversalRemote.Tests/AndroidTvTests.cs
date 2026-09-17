using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Core;
using UniversalRemote.Remote.Provider.AndroidTv;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace UniversalRemote.Remote.Tests;

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
    public void Manual_pairing_accepts_private_local_ip_and_rejects_public_or_hostname()
    {
        var provider = new AndroidTvPairingProvider(new InMemoryAndroidTvCredentialStore());

        var local = provider.CreateManualCandidate("192.168.1.86", "TCL salon");
        Assert.NotNull(local);
        Assert.Equal("androidtv", local!.ProviderId);
        Assert.Equal("192.168.1.86", local.DeviceKey);
        Assert.Equal("TCL salon", local.DisplayName);

        Assert.Null(provider.CreateManualCandidate("8.8.8.8"));
        Assert.Null(provider.CreateManualCandidate("tv.example.com"));
        Assert.Null(provider.CreateManualCandidate("127.0.0.1"));
    }

    [Fact]
    public void Android_tv_registration_exposes_manual_pairing_provider()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IAndroidTvCredentialStore, InMemoryAndroidTvCredentialStore>();
        services.AddUniversalRemoteAndroidTv();
        using var provider = services.BuildServiceProvider();

        var manual = provider.GetServices<IManualPairingProvider>().Single(x => x.Id == AndroidTvPairingProvider.ProviderId);
        var automatic = provider.GetServices<IDevicePairingProvider>().Single(x => x.Id == AndroidTvPairingProvider.ProviderId);
        Assert.Same(automatic, manual);
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

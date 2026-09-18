using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Discovery;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Targets;
using Xunit;

namespace UniversalRemote.Media.Tests;

public sealed class MediaPlaybackTargetTests
{
    [Fact]
    public async Task Discovery_always_exposes_local_device_as_launchable()
    {
        var service = new PlaybackTargetDiscovery(new FakeDeviceRepository([]), new FakeDiscovery([]));

        var targets = await service.DiscoverAsync();

        var local = Assert.Single(targets.Where(x => x.Target.Kind == PlaybackTargetKind.LocalDevice));
        Assert.True(local.CanLaunch);
        Assert.Contains(PlaybackCapability.Start, local.Target.Capabilities);
    }

    [Fact]
    public async Task Paired_android_tv_is_visible_but_not_falsely_advertised_as_direct_launch_transport()
    {
        var device = new Device(Guid.NewGuid(), "TV Salon",
        [
            new DeviceRoute("androidtv", "paired-tv", [RemoteActions.PlayPause, RemoteActions.FastForward])
        ]);
        var service = new PlaybackTargetDiscovery(new FakeDeviceRepository([device]), new FakeDiscovery([]));

        var targets = await service.DiscoverAsync();

        var androidTv = Assert.Single(targets.Where(x => x.Target.Kind == PlaybackTargetKind.AndroidTv));
        Assert.Equal(device.Id, androidTv.Target.DeviceId);
        Assert.False(androidTv.CanLaunch);
        Assert.Contains(PlaybackCapability.Pause, androidTv.Target.Capabilities);
        Assert.Contains(PlaybackCapability.Seek, androidTv.Target.Capabilities);
        Assert.DoesNotContain(PlaybackCapability.Start, androidTv.Target.Capabilities);
    }

    [Fact]
    public async Task Google_cast_mdns_receiver_is_discovered_without_exposing_network_address_in_target_id()
    {
        var discovered = new DiscoveredDevice
        {
            DiscoveryId = "living-room._googlecast._tcp.local|192.168.1.44",
            DisplayName = "Chromecast Salon",
            HostName = "cast-salon.local",
            Addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.44" },
            Services = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_googlecast._tcp.local" },
            Sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mdns" },
            Metadata = new Dictionary<string, string>()
        };
        var service = new PlaybackTargetDiscovery(new FakeDeviceRepository([]), new FakeDiscovery([discovered]));

        var targets = await service.DiscoverAsync();

        var cast = Assert.Single(targets.Where(x => x.Target.Kind == PlaybackTargetKind.Cast));
        Assert.False(cast.CanLaunch);
        Assert.DoesNotContain("192.168.1.44", cast.Target.Id, StringComparison.Ordinal);
        Assert.Equal("cast-salon.local", cast.AddressHint);
    }

    [Fact]
    public void Target_selection_defaults_to_local_and_can_switch_without_changing_media_source()
    {
        IPlaybackTargetSelection selection = new PlaybackTargetSelection();
        Assert.Equal("local-device", selection.Selected.Id);
        var target = new PlaybackTarget("remote:1", "Salon", PlaybackTargetKind.RemoteDevice, []);

        selection.Select(target);

        Assert.Same(target, selection.Selected);
    }

    [Fact]
    public void Dependency_injection_registers_target_discovery_and_selection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceRepository>(new FakeDeviceRepository([]));
        services.AddSingleton<IDeviceDiscovery>(new FakeDiscovery([]));
        services.AddUniversalRemoteMediaTargets();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IPlaybackTargetDiscovery>());
        Assert.NotNull(provider.GetRequiredService<IPlaybackTargetSelection>());
    }

    private sealed class FakeDeviceRepository(IReadOnlyList<Device> devices) : IDeviceRepository
    {
        public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(devices.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(devices);
    }

    private sealed class FakeDiscovery(IReadOnlyList<DiscoveredDevice> devices) : IDeviceDiscovery
    {
        public Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(DiscoveryScanOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(devices);
    }
}

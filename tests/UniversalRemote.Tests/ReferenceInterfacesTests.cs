using UniversalRemote.Abstractions;
using UniversalRemote.Core;
using UniversalRemote.Presentation;
using UniversalRemote.Provider.AndroidTv;
using UniversalRemote.Theming;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class ReferenceInterfacesTests
{
    [Fact]
    public void Reference_demo_does_not_add_capabilities_to_a_real_device_projection()
    {
        var device = new Device(Guid.NewGuid(), "Limited TV", [new DeviceRoute("test", "tv", [RemoteActions.VolumeUp])]);
        var model = new CapabilityRemoteUiModelBuilder().Build(device);
        Assert.Equal(RemoteActions.VolumeUp, Assert.Single(model.Controls).Action);
        Assert.DoesNotContain(model.Controls, c => c.Action == RemoteActions.Netflix || c.Action == RemoteActions.Hdmi1);
    }

    [Fact]
    public async Task Unsupported_reference_shortcut_never_reaches_a_provider()
    {
        var device = new Device(Guid.NewGuid(), "TV", [new DeviceRoute("capture", "tv", [RemoteActions.Home])]);
        var capture = new CaptureProvider();
        var dispatcher = new CommandDispatcher(new InMemoryDeviceRepository([device]), new ProviderResolver([capture]), new CommandOptions());
        var result = await dispatcher.ExecuteAsync(device.Id, RemoteActions.Netflix);
        Assert.Equal(RemoteErrorCode.UnsupportedAction, result.Error);
        Assert.Equal(0, capture.Calls);
    }

    [Fact]
    public void Custom_and_new_actions_survive_all_layout_projections()
    {
        var custom = new RemoteAction("custom.lamp.scene");
        var device = new Device(Guid.NewGuid(), "Extended TV", [new DeviceRoute("test", "tv", [RemoteActions.Digit1, RemoteActions.Guide, custom])]);
        var model = new CapabilityRemoteUiModelBuilder().Build(device);
        foreach (var layout in BuiltInRemoteStyles.Layouts)
        {
            var actions = RemoteLayoutProjection.Sections(model, layout).SelectMany(s => s.Controls).Select(c => c.Action).ToArray();
            Assert.Equal(3, actions.Length);
            Assert.Contains(custom, actions);
            Assert.Contains(RemoteActions.Digit1, actions);
            Assert.Contains(RemoteActions.Guide, actions);
        }
    }

    [Fact]
    public async Task Every_advertised_android_key_has_a_protocol_mapping_before_pairing()
    {
        await using var provider = new AndroidTvRemoteProvider(new InMemoryAndroidTvCredentialStore());
        var route = new DeviceRoute(AndroidTvRemoteProvider.ProviderId, "192.168.1.10", AndroidTvRemoteProvider.Capabilities);
        foreach (var action in AndroidTvRemoteProvider.Capabilities)
        {
            var result = await provider.ExecuteAsync(route, action, CancellationToken.None);
            Assert.Equal(RemoteErrorCode.PairingRequired, result.Error);
        }
        Assert.DoesNotContain(RemoteActions.Netflix, AndroidTvRemoteProvider.Capabilities);
        Assert.DoesNotContain(RemoteActions.YouTube, AndroidTvRemoteProvider.Capabilities);
    }

    private sealed class CaptureProvider : IRemoteProvider
    {
        public int Calls { get; private set; }
        public string Id => "capture";
        public SupportLevel SupportLevel => SupportLevel.Experimental;
        public Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(RemoteResult.Accepted());
        }
    }
}

using UniversalRemote.Abstractions;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Control;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class MediaRemoteControlTests
{
    [Fact]
    public async Task Local_target_has_no_physical_remote_commands()
    {
        var sut = new MediaRemoteControl(new FakeDeviceRepository(), new FakeRemoteControl());

        var state = await sut.GetStateAsync(PlaybackTargets.LocalDevice);

        Assert.False(state.HasRemoteDevice);
        Assert.Empty(state.Commands);
    }

    [Fact]
    public async Task Remote_overlay_exposes_only_device_capabilities()
    {
        var id = Guid.NewGuid();
        var device = new Device(id, "TV salon",
        [
            new DeviceRoute("test", "route", [
                RemoteActions.VolumeUp,
                RemoteActions.VolumeDown,
                RemoteActions.Ok,
                RemoteActions.Back])
        ]);
        var repo = new FakeDeviceRepository(device);
        var sut = new MediaRemoteControl(repo, new FakeRemoteControl());
        var target = new PlaybackTarget("tv:1", "TV salon", PlaybackTargetKind.AndroidTv,
            [PlaybackCapability.Pause], id);

        var state = await sut.GetStateAsync(target);

        Assert.True(state.HasRemoteDevice);
        Assert.True(state.Supports(RemoteActions.VolumeUp));
        Assert.True(state.Supports(RemoteActions.Ok));
        Assert.False(state.Supports(RemoteActions.Guide));
        Assert.False(state.Supports(RemoteActions.Home));
    }

    [Fact]
    public async Task Unsupported_command_is_rejected_before_transport()
    {
        var id = Guid.NewGuid();
        var device = new Device(id, "TV salon",
            [new DeviceRoute("test", "route", [RemoteActions.VolumeUp])]);
        var transport = new FakeRemoteControl();
        var sut = new MediaRemoteControl(new FakeDeviceRepository(device), transport);
        var target = new PlaybackTarget("tv:1", "TV salon", PlaybackTargetKind.RemoteDevice,
            Array.Empty<PlaybackCapability>(), id);

        var result = await sut.ExecuteAsync(target, RemoteActions.Guide);

        Assert.False(result.IsSuccess);
        Assert.Equal(RemoteErrorCode.UnsupportedAction, result.Error);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task Supported_command_is_forwarded_once()
    {
        var id = Guid.NewGuid();
        var device = new Device(id, "TV salon",
            [new DeviceRoute("test", "route", [RemoteActions.MuteToggle])]);
        var transport = new FakeRemoteControl();
        var sut = new MediaRemoteControl(new FakeDeviceRepository(device), transport);
        var target = new PlaybackTarget("tv:1", "TV salon", PlaybackTargetKind.RemoteDevice,
            Array.Empty<PlaybackCapability>(), id);

        var result = await sut.ExecuteAsync(target, RemoteActions.MuteToggle);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, transport.CallCount);
        Assert.Equal(id, transport.LastDeviceId);
        Assert.Equal(RemoteActions.MuteToggle, transport.LastAction);
    }

    private sealed class FakeDeviceRepository(params Device[] devices) : IDeviceRepository
    {
        private readonly IReadOnlyList<Device> all = devices;
        public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(all.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(all);
    }

    private sealed class FakeRemoteControl : IRemoteControl
    {
        public int CallCount { get; private set; }
        public Guid? LastDeviceId { get; private set; }
        public RemoteAction? LastAction { get; private set; }

        public Task<RemoteResult> ExecuteAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastDeviceId = deviceId;
            LastAction = action;
            return Task.FromResult(RemoteResult.Accepted());
        }
    }
}

using DomainRelay.Abstractions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Core;
using UniversalRemote.Remote.Presentation;
using UniversalRemote.Remote.Theming;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class PresentationTests
{
    [Fact]
    public void Ui_model_is_derived_from_capabilities_not_provider_name()
    {
        var capabilities = new[]
        {
            RemoteActions.PowerToggle,
            RemoteActions.VolumeUp,
            RemoteActions.VolumeDown,
            RemoteActions.Ok,
            RemoteActions.Home
        };
        var device = new Device(
            Guid.NewGuid(),
            "Living room TV",
            [new DeviceRoute("any-provider", "device-1", capabilities)]);

        var model = new CapabilityRemoteUiModelBuilder().Build(device);

        Assert.Equal(device.Id, model.DeviceId);
        Assert.Equal(capabilities.Length, model.Controls.Count);
        Assert.Contains(model.Controls, control => control.Action == RemoteActions.PowerToggle);
        Assert.DoesNotContain(model.Controls, control => control.Action == RemoteActions.MuteToggle);
        Assert.Equal(["power", "navigation", "audio"], model.Sections.Select(section => section.Id).ToArray());
    }

    [Fact]
    public void Unknown_capability_remains_renderable_in_extras()
    {
        var custom = new RemoteAction("media.subtitle.toggle");
        var device = new Device(
            Guid.NewGuid(),
            "Extensible device",
            [new DeviceRoute("future-provider", "device-2", [custom])]);

        var model = new CapabilityRemoteUiModelBuilder().Build(device);

        var section = Assert.Single(model.Sections);
        Assert.Equal("extras", section.Id);
        var control = Assert.Single(section.Controls);
        Assert.Equal(custom, control.Action);
        Assert.Equal(RemoteControlRole.Extra, control.Role);
    }

    [Fact]
    public void Classic_and_minimal_are_renderer_metadata_for_the_same_model()
    {
        var device = new Device(
            Guid.NewGuid(),
            "TV",
            [new DeviceRoute("provider", "key", [RemoteActions.PowerToggle, RemoteActions.VolumeUp, RemoteActions.Ok])]);
        var model = new CapabilityRemoteUiModelBuilder().Build(device);

        Assert.Equal(3, model.Controls.Count);
        Assert.Equal("classic", BuiltInRemoteStyles.Classic.Id);
        Assert.Equal("minimal", BuiltInRemoteStyles.Minimal.Id);
        Assert.Equal(48, BuiltInRemoteStyles.DefaultTheme.Tokens.MinimumTouchTargetDp);
        Assert.All(BuiltInRemoteStyles.Layouts, layout => Assert.Contains("power", layout.SectionOrder));
    }

    [Fact]
    public async Task GetRemoteUiModel_traverses_mediator()
    {
        var id = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddUniversalRemoteApplication();
        services.AddSingleton<IDeviceRegistrar, InMemoryDeviceRepository>();
        services.AddSingleton<IDeviceRepository>(new SingleDeviceRepository(
            new Device(id, "Demo", [new DeviceRoute("sim", "1", [RemoteActions.PowerToggle, RemoteActions.Ok])])));
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = root.CreateScope();

        var model = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetRemoteUiModel(id));

        Assert.NotNull(model);
        Assert.Equal(id, model.DeviceId);
        Assert.Equal(2, model.Controls.Count);
    }

    [Fact]
    public async Task Empty_device_id_is_rejected_by_validation_pipeline()
    {
        var services = new ServiceCollection();
        services.AddUniversalRemoteApplication();
        services.AddSingleton<IDeviceRegistrar, InMemoryDeviceRepository>();
        services.AddSingleton<IDeviceRepository>(new SingleDeviceRepository(null));
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetRemoteUiModel(Guid.Empty)));
    }

    private sealed class SingleDeviceRepository(Device? device) : IDeviceRepository
    {
        public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(device is not null && device.Id == id ? device : null);

        public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Device>>(device is null ? [] : [device]);
    }
}

using UniversalRemote.Abstractions;
using UniversalRemote.Core;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class CommandEngineTests
{
    private static readonly Guid Id = Guid.Parse("163f7143-6d50-40ef-afc8-f0d31dd190ee");
    private static Device Device(params DeviceRoute[] routes) => new(Id, "Test", routes);
    private static DeviceRoute Route(string provider = "test", params RemoteAction[] actions)
        => new(provider, "test-device", actions.Length == 0 ? [RemoteActions.VolumeUp] : actions);
    private static CommandDispatcher Engine(Device? device, params IRemoteProvider[] providers)
        => new(new Repository(device), new ProviderResolver(providers), new CommandOptions());

    [Fact]
    public async Task Supported_action_is_sent_once()
    {
        var provider = new TestProvider();
        var result = await Engine(Device(Route()), provider).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryState.Accepted, result.Delivery);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Unknown_device_never_sends()
    {
        var provider = new TestProvider();
        var result = await Engine(null, provider).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(RemoteErrorCode.DeviceNotFound, result.Error);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Unsupported_action_never_sends()
    {
        var provider = new TestProvider();
        var result = await Engine(Device(Route()), provider).ExecuteAsync(Id, RemoteActions.Home);
        Assert.Equal(RemoteErrorCode.UnsupportedAction, result.Error);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Missing_provider_is_reported()
    {
        var result = await Engine(Device(Route())).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(RemoteErrorCode.ProviderUnavailable, result.Error);
        Assert.Equal(DeliveryState.NotSent, result.Delivery);
    }

    [Fact]
    public async Task Resolution_uses_capabilities_of_route_not_just_device_union()
    {
        var first = new TestProvider();
        var second = new TestProvider("second");
        var device = Device(Route("test", RemoteActions.Home), Route("second", RemoteActions.VolumeUp));
        await Engine(device, first, second).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(0, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task Resolution_obeys_device_route_order()
    {
        var first = new TestProvider();
        var second = new TestProvider("second");
        await Engine(Device(Route("second"), Route()), first, second).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(0, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task Missing_route_registration_can_be_skipped_before_any_send()
    {
        var provider = new TestProvider();
        var result = await Engine(Device(Route("absent"), Route()), provider).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void Duplicate_provider_ids_fail_fast()
        => Assert.Throws<ArgumentException>(() => new ProviderResolver([new TestProvider(), new TestProvider()]));

    [Fact]
    public async Task Ambiguous_transport_failure_is_not_retried_or_sent_to_fallback()
    {
        var first = new TestProvider { Execute = _ => throw new HttpRequestException("token=secret") };
        var second = new TestProvider("second");
        var result = await Engine(Device(Route(), Route("second")), first, second).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(RemoteErrorCode.TransportFailure, result.Error);
        Assert.Equal(DeliveryState.Unknown, result.Delivery);
        Assert.DoesNotContain("secret", result.ToString());
        Assert.Equal(1, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task Pairing_error_is_preserved_without_fallback()
    {
        var first = new TestProvider { Execute = _ => Task.FromResult(RemoteResult.Failed(RemoteErrorCode.PairingRequired)) };
        var second = new TestProvider("second");
        var result = await Engine(Device(Route(), Route("second")), first, second).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(RemoteErrorCode.PairingRequired, result.Error);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task Precancelled_request_never_sends()
    {
        var provider = new TestProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine(Device(Route()), provider)
            .ExecuteAsync(Id, RemoteActions.VolumeUp, cancellation.Token));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Cancellation_during_send_remains_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new TestProvider { Execute = async ct => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, ct); return RemoteResult.Accepted(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine(Device(Route()), provider)
            .ExecuteAsync(Id, RemoteActions.VolumeUp, cancellation.Token));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Cooperative_timeout_is_unknown_and_not_retried()
    {
        var provider = new TestProvider { Execute = async ct => { await Task.Delay(Timeout.Infinite, ct); return RemoteResult.Accepted(); } };
        var engine = new CommandDispatcher(new Repository(Device(Route())), new ProviderResolver([provider]),
            new CommandOptions { Timeout = TimeSpan.FromMilliseconds(25) });
        var result = await engine.ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(RemoteErrorCode.Timeout, result.Error);
        Assert.Equal(DeliveryState.Unknown, result.Delivery);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Unexpected_provider_exception_is_sanitized()
    {
        var provider = new TestProvider { Execute = _ => throw new InvalidOperationException("private endpoint") };
        var result = await Engine(Device(Route()), provider).ExecuteAsync(Id, RemoteActions.VolumeUp);
        Assert.Equal(RemoteErrorCode.ProviderFailure, result.Error);
        Assert.Equal(DeliveryState.Unknown, result.Delivery);
    }

    [Fact]
    public void Capability_snapshot_cannot_be_mutated_by_caller()
    {
        var actions = new List<RemoteAction> { RemoteActions.VolumeUp };
        var route = new DeviceRoute("test", "device", actions);
        var routes = new List<DeviceRoute> { route };
        var device = new Device(Id, "Test", routes);
        actions.Clear(); routes.Clear();
        Assert.Contains(RemoteActions.VolumeUp, device.Capabilities);
        Assert.Single(device.Routes);
    }

    [Fact]
    public void Stable_action_ids_compare_by_value()
        => Assert.Equal(RemoteActions.VolumeUp, new RemoteAction("volume.up"));

    [Fact]
    public void Duplicate_device_routes_are_rejected()
        => Assert.Throws<ArgumentException>(() => Device(Route(), Route()));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    public void Invalid_timeout_is_rejected(int seconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CommandDispatcher(new Repository(null), new ProviderResolver([]),
            new CommandOptions { Timeout = TimeSpan.FromSeconds(seconds) }));

    private sealed class Repository(Device? device) : IDeviceRepository
    {
        public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(device?.Id == id ? device : null);
        public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Device>>(device is null ? [] : [device]);
    }
    private sealed class TestProvider(string id = "test") : IRemoteProvider
    {
        public string Id => id;
        public SupportLevel SupportLevel => SupportLevel.Experimental;
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<RemoteResult>> Execute { get; init; } = _ => Task.FromResult(RemoteResult.Accepted());
        public Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
        { Calls++; return Execute(cancellationToken); }
    }
}

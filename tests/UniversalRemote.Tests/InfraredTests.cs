using System.Text.Json;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Provider.GenericIr;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class InfraredTests
{
    [Fact]
    public void Built_in_bench_profile_is_explicitly_unverified()
    {
        var profile = BuiltInIrProfiles.BenchSyntheticTv;
        Assert.False(profile.Verified);
        Assert.Equal(1, profile.Version);
        Assert.Contains(RemoteActions.PowerToggle, profile.Capabilities);
        Assert.Contains(RemoteActions.VolumeUp, profile.Capabilities);
        Assert.Contains(RemoteActions.VolumeDown, profile.Capabilities);
    }

    [Fact]
    public void Unknown_json_member_is_rejected()
    {
        var json = ValidJson().Replace("\"commands\"", "\"unexpected\":true,\"commands\"");
        Assert.Throws<FormatException>(() => IrProfileParser.Parse(json));
    }

    [Fact]
    public void Future_profile_version_is_rejected()
        => Assert.Throws<FormatException>(() => IrProfileParser.Parse(ValidJson().Replace("\"version\":1", "\"version\":2")));

    [Fact]
    public void Duplicate_action_is_rejected()
    {
        var json = ValidJson(command2Action: "power.toggle");
        Assert.Throws<FormatException>(() => IrProfileParser.Parse(json));
    }

    [Theory]
    [InlineData("[0,560]")]
    [InlineData("[-1,560]")]
    [InlineData("[1000000,1000000]")]
    public void Invalid_patterns_are_rejected(string pattern)
    {
        var json = ValidJson().Replace("[9000,4500,560,560]", pattern);
        Assert.Throws<FormatException>(() => IrProfileParser.Parse(json));
    }

    [Fact]
    public void Too_many_pattern_values_are_rejected()
    {
        var values = string.Join(',', Enumerable.Repeat("1", InfraredSignalLimits.MaxPatternValues + 1));
        var json = ValidJson().Replace("[9000,4500,560,560]", $"[{values}]");
        Assert.Throws<FormatException>(() => IrProfileParser.Parse(json));
    }

    [Fact]
    public void Oversized_json_is_rejected_before_deserialization()
    {
        var json = ValidJson().Replace("\"Test\"", $"\"{new string('x', IrProfileParser.MaxJsonBytes)}\"");
        Assert.Throws<FormatException>(() => IrProfileParser.Parse(json));
    }

    [Fact]
    public async Task Accepted_platform_transmission_is_accepted_once()
    {
        var transmitter = new FakeTransmitter();
        var provider = Provider(transmitter);
        var result = await provider.ExecuteAsync(Route(), RemoteActions.PowerToggle, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryState.Accepted, result.Delivery);
        Assert.Equal(1, transmitter.Calls);
    }

    [Fact]
    public async Task No_emitter_is_not_sent()
    {
        var transmitter = new FakeTransmitter { Info = new InfraredEmitterInfo(false, [], "ir.emitter.unavailable") };
        var result = await Provider(transmitter).ExecuteAsync(Route(), RemoteActions.PowerToggle, default);
        Assert.Equal(RemoteErrorCode.ProviderUnavailable, result.Error);
        Assert.Equal(DeliveryState.NotSent, result.Delivery);
        Assert.Equal(0, transmitter.Calls);
    }

    [Fact]
    public async Task Unsupported_frequency_is_not_sent()
    {
        var transmitter = new FakeTransmitter
        {
            Info = new InfraredEmitterInfo(true, [new InfraredFrequencyRange(30000, 36000)], "ir.ready")
        };
        var result = await Provider(transmitter).ExecuteAsync(Route(), RemoteActions.PowerToggle, default);
        Assert.Equal(RemoteErrorCode.ProviderUnavailable, result.Error);
        Assert.Equal(0, transmitter.Calls);
    }

    [Fact]
    public async Task Missing_profile_requires_configuration_without_emission()
    {
        var transmitter = new FakeTransmitter();
        var provider = new GenericIrRemoteProvider(transmitter, new InMemoryIrProfileCatalog());
        var result = await provider.ExecuteAsync(Route(), RemoteActions.PowerToggle, default);
        Assert.Equal(RemoteErrorCode.PairingRequired, result.Error);
        Assert.Equal(0, transmitter.Calls);
    }

    [Fact]
    public async Task Unknown_platform_outcome_is_never_retried()
    {
        var transmitter = new FakeTransmitter { Outcome = InfraredTransmitResult.Unknown() };
        var result = await Provider(transmitter).ExecuteAsync(Route(), RemoteActions.PowerToggle, default);
        Assert.Equal(RemoteErrorCode.TransportFailure, result.Error);
        Assert.Equal(DeliveryState.Unknown, result.Delivery);
        Assert.Equal(1, transmitter.Calls);
    }

    [Fact]
    public async Task Wrong_provider_route_is_not_sent()
    {
        var transmitter = new FakeTransmitter();
        var route = new DeviceRoute("other", BuiltInIrProfiles.BenchSyntheticTv.Id, [RemoteActions.PowerToggle]);
        var result = await Provider(transmitter).ExecuteAsync(route, RemoteActions.PowerToggle, default);
        Assert.Equal(RemoteErrorCode.ProviderUnavailable, result.Error);
        Assert.Equal(0, transmitter.Calls);
    }

    [Fact]
    public async Task Unsupported_action_is_not_sent()
    {
        var transmitter = new FakeTransmitter();
        var result = await Provider(transmitter).ExecuteAsync(Route(), RemoteActions.Home, default);
        Assert.Equal(RemoteErrorCode.UnsupportedAction, result.Error);
        Assert.Equal(0, transmitter.Calls);
    }

    [Fact]
    public async Task Cancellation_is_propagated_before_emission()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transmitter = new FakeTransmitter();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider(transmitter).ExecuteAsync(Route(), RemoteActions.PowerToggle, cancellation.Token));
        Assert.Equal(0, transmitter.Calls);
    }

    private static GenericIrRemoteProvider Provider(FakeTransmitter transmitter)
        => new(transmitter, new InMemoryIrProfileCatalog([BuiltInIrProfiles.BenchSyntheticTv]));

    private static DeviceRoute Route()
    {
        var profile = BuiltInIrProfiles.BenchSyntheticTv;
        return new DeviceRoute(GenericIrRemoteProvider.ProviderId, profile.Id, profile.Capabilities);
    }

    private static string ValidJson(string command2Action = "volume.up") => $$"""
    {
      "version":1,
      "id":"test.profile",
      "displayName":"Test",
      "verified":false,
      "source":"unit-test",
      "carrierFrequencyHz":38000,
      "commands":[
        {"actionId":"power.toggle","patternMicroseconds":[9000,4500,560,560]},
        {"actionId":"{{command2Action}}","patternMicroseconds":[9000,2250,560,560]}
      ]
    }
    """;

    private sealed class FakeTransmitter : IInfraredTransmitter
    {
        public InfraredEmitterInfo Info { get; init; } = new(true, [new InfraredFrequencyRange(36000, 40000)], "ir.ready");
        public InfraredTransmitResult Outcome { get; init; } = InfraredTransmitResult.Accepted();
        public int Calls { get; private set; }

        public ValueTask<InfraredEmitterInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Info);
        }

        public Task<InfraredTransmitResult> TransmitAsync(int carrierFrequencyHz, IReadOnlyList<int> patternMicroseconds, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Outcome);
        }
    }
}

using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Provider.LG;
using UniversalRemote.Remote.Provider.Samsung;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class Sprint7ProviderTests
{
    [Fact]
    public void SamsungMatcherRecognizesSamsungSsdpMetadata()
    {
        var provider = new SamsungPairingProvider(new MemorySamsungStore());
        var match = provider.Match(new PairingProbe("Samsung TV", ["192.168.1.10"], ["urn:samsung.com:device:RemoteControlReceiver:1"]));
        Assert.NotNull(match); Assert.Equal(SamsungPairingProvider.ProviderId, match!.ProviderId);
    }

    [Fact]
    public void LgMatcherRecognizesWebOsMetadata()
    {
        var provider = new LgPairingProvider(new MemoryLgStore());
        var match = provider.Match(new PairingProbe("LG webOS TV", ["192.168.1.11"], ["urn:lge-com:service:webos-second-screen:1"]));
        Assert.NotNull(match); Assert.Equal(LgPairingProvider.ProviderId, match!.ProviderId);
    }

    [Fact]
    public void NetworkTvProvidersDoNotAdvertisePowerToggleWithoutVerifiedWake()
    {
        Assert.DoesNotContain(RemoteActions.PowerToggle, LgRemoteProvider.Capabilities);
        Assert.DoesNotContain(RemoteActions.PowerToggle, SamsungRemoteProvider.Capabilities);
    }

    private sealed class MemorySamsungStore : ISamsungCredentialStore
    {
        public Task<SamsungCredentials?> GetAsync(string host, CancellationToken cancellationToken = default) => Task.FromResult<SamsungCredentials?>(null);
        public Task SaveAsync(SamsungCredentials credentials, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MemoryLgStore : ILgCredentialStore
    {
        public Task<LgCredentials?> GetAsync(string host, CancellationToken cancellationToken = default) => Task.FromResult<LgCredentials?>(null);
        public Task SaveAsync(LgCredentials credentials, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

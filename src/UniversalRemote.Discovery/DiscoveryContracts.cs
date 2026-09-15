using System.Collections.Frozen;

namespace UniversalRemote.Discovery;

public sealed record DiscoveryScanOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);
    public IReadOnlyList<string> MdnsServiceTypes { get; init; } =
        ["_googlecast._tcp.local", "_androidtvremote2._tcp.local", "_fbx-api._tcp.local", "_airplay._tcp.local", "_http._tcp.local"];

    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Discovery timeout must be between 0 and 30 seconds.");
    }
}

public sealed record DiscoveryCandidate
{
    public required string SourceId { get; init; }
    public required string StableKey { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? HostName { get; init; }
    public IReadOnlySet<string> Addresses { get; init; } = Array.Empty<string>().ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> Services { get; init; } = Array.Empty<string>().ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset SeenAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DiscoveredDevice
{
    public required string DiscoveryId { get; init; }
    public required string DisplayName { get; init; }
    public string? HostName { get; init; }
    public required IReadOnlySet<string> Addresses { get; init; }
    public required IReadOnlySet<string> Services { get; init; }
    public required IReadOnlySet<string> Sources { get; init; }
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}

public interface IDiscoverySource
{
    string Id { get; }
    Task<IReadOnlyList<DiscoveryCandidate>> DiscoverAsync(DiscoveryScanOptions options, CancellationToken cancellationToken);
}

public interface IDiscoveryConsolidator
{
    IReadOnlyList<DiscoveredDevice> Consolidate(IEnumerable<DiscoveryCandidate> candidates);
}

public interface IDeviceDiscovery
{
    Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(DiscoveryScanOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Platform hook used when the OS requires an explicit multicast/network discovery lease.</summary>
public interface IDiscoveryNetworkLease
{
    ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}

public sealed class NoopDiscoveryNetworkLease : IDiscoveryNetworkLease
{
    public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable>(NoopLease.Instance);
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public static NoopLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

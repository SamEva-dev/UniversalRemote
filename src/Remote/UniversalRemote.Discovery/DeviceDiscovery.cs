using System.Net.Sockets;

namespace UniversalRemote.Remote.Discovery;

public sealed class DeviceDiscovery(
    IEnumerable<IDiscoverySource> sources,
    IDiscoveryConsolidator consolidator,
    IDiscoveryNetworkLease networkLease) : IDeviceDiscovery
{
    private readonly IDiscoverySource[] sources = sources?.ToArray() ?? throw new ArgumentNullException(nameof(sources));

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(DiscoveryScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new DiscoveryScanOptions();
        options.Validate();
        if (sources.Length == 0) return Array.Empty<DiscoveredDevice>();

        await using var lease = await networkLease.AcquireAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        var tasks = sources.Select(source => SafeDiscoverAsync(source, options, timeout.Token)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return consolidator.Consolidate(results.SelectMany(x => x));
    }

    private static async Task<IReadOnlyList<DiscoveryCandidate>> SafeDiscoverAsync(
        IDiscoverySource source,
        DiscoveryScanOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.DiscoverAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<DiscoveryCandidate>();
        }
        catch (SocketException)
        {
            return Array.Empty<DiscoveryCandidate>();
        }
        catch (IOException)
        {
            return Array.Empty<DiscoveryCandidate>();
        }
    }
}

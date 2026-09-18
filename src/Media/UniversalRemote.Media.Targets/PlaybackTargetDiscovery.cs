using System.Security.Cryptography;
using System.Text;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Discovery;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Targets;

public sealed class PlaybackTargetDiscovery(
    IDeviceRepository devices,
    IDeviceDiscovery discovery) : IPlaybackTargetDiscovery
{
    private const string AndroidTvProviderId = "androidtv";
    private const string CastService = "_googlecast._tcp.local";

    public async Task<IReadOnlyList<PlaybackTargetCandidate>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<PlaybackTargetCandidate>
        {
            new()
            {
                Target = PlaybackTargets.LocalDevice,
                CanLaunch = true,
                Status = "Disponible"
            }
        };

        var registered = await devices.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var device in registered.Where(HasAndroidTvRoute))
        {
            result.Add(new PlaybackTargetCandidate
            {
                Target = new PlaybackTarget(
                    $"androidtv:{device.Id:N}",
                    device.DisplayName,
                    PlaybackTargetKind.AndroidTv,
                    RemoteTransportCapabilities(device),
                    device.Id),
                // Android TV Remote v2 currently gives us remote-control key injection, not an arbitrary
                // media URL launch contract. Keep it visible but non-launchable until a launch transport exists.
                CanLaunch = false,
                Status = "Android TV associé • diffusion directe bientôt disponible"
            });
        }

        IReadOnlyList<DiscoveredDevice> network;
        try
        {
            network = await discovery.DiscoverAsync(new DiscoveryScanOptions
            {
                Timeout = TimeSpan.FromSeconds(3),
                MdnsServiceTypes = [CastService, "_androidtvremote2._tcp.local"]
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            network = Array.Empty<DiscoveredDevice>();
        }

        foreach (var candidate in network.Where(IsCast))
        {
            var stableId = Hash(candidate.DiscoveryId);
            result.Add(new PlaybackTargetCandidate
            {
                Target = new PlaybackTarget(
                    $"cast:{stableId}",
                    string.IsNullOrWhiteSpace(candidate.DisplayName) ? "Google Cast" : candidate.DisplayName,
                    PlaybackTargetKind.Cast,
                    [PlaybackCapability.Start, PlaybackCapability.Stop, PlaybackCapability.Pause, PlaybackCapability.Resume, PlaybackCapability.Seek]),
                // Discovery is implemented now. A Google Cast sender transport is deliberately not faked:
                // the candidate remains disabled until that transport is registered in a later implementation.
                CanLaunch = false,
                Status = "Cast détecté • transport de diffusion non installé",
                AddressHint = candidate.HostName
            });
        }

        return result
            .GroupBy(x => x.Target.Id, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderByDescending(x => x.Target.Kind == PlaybackTargetKind.LocalDevice)
            .ThenBy(x => x.Target.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool HasAndroidTvRoute(Device device)
        => device.Routes.Any(route => string.Equals(route.ProviderId, AndroidTvProviderId, StringComparison.Ordinal));

    private static IReadOnlyCollection<PlaybackCapability> RemoteTransportCapabilities(Device device)
    {
        var capabilities = new List<PlaybackCapability>();
        if (device.Capabilities.Contains(RemoteActions.PlayPause))
        {
            capabilities.Add(PlaybackCapability.Pause);
            capabilities.Add(PlaybackCapability.Resume);
        }
        if (device.Capabilities.Contains(RemoteActions.Rewind) || device.Capabilities.Contains(RemoteActions.FastForward))
            capabilities.Add(PlaybackCapability.Seek);
        if (capabilities.Count > 0) capabilities.Add(PlaybackCapability.Stop);
        return capabilities;
    }

    private static bool IsCast(DiscoveredDevice device)
        => device.Services.Any(service =>
            service.Contains("_googlecast._tcp", StringComparison.OrdinalIgnoreCase));

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 10)).ToLowerInvariant();
    }
}

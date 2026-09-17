using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

namespace UniversalRemote.Remote.Discovery;

public sealed class DiscoveryConsolidator : IDiscoveryConsolidator
{
    public IReadOnlyList<DiscoveredDevice> Consolidate(IEnumerable<DiscoveryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var snapshots = candidates.Where(IsValid).ToArray();
        if (snapshots.Length == 0) return Array.Empty<DiscoveredDevice>();

        var groups = new List<List<DiscoveryCandidate>>();
        foreach (var candidate in snapshots.OrderBy(x => x.SeenAt))
        {
            var group = groups.FirstOrDefault(existing => existing.Any(x => SameDevice(x, candidate)));
            if (group is null) groups.Add([candidate]);
            else group.Add(candidate);
        }

        return groups.Select(Merge).OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsValid(DiscoveryCandidate item)
        => !string.IsNullOrWhiteSpace(item.SourceId) && !string.IsNullOrWhiteSpace(item.StableKey);

    private static bool SameDevice(DiscoveryCandidate left, DiscoveryCandidate right)
    {
        if (string.Equals(left.StableKey, right.StableKey, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(left.HostName) && !string.IsNullOrWhiteSpace(right.HostName) &&
            string.Equals(NormalizeHost(left.HostName), NormalizeHost(right.HostName), StringComparison.OrdinalIgnoreCase)) return true;
        return left.Addresses.Count > 0 && right.Addresses.Any(a => left.Addresses.Contains(a));
    }

    private static DiscoveredDevice Merge(IReadOnlyList<DiscoveryCandidate> items)
    {
        var addresses = items.SelectMany(x => x.Addresses).Where(x => !string.IsNullOrWhiteSpace(x)).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var services = items.SelectMany(x => x.Services).Where(x => !string.IsNullOrWhiteSpace(x)).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var sources = items.Select(x => x.SourceId).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in items.SelectMany(x => x.Metadata)) metadata.TryAdd(pair.Key, pair.Value);
        var host = items.Select(x => x.HostName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var displayName = items.Select(x => x.DisplayName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? host ?? "Appareil réseau détecté";
        var identitySeed = host ?? addresses.FirstOrDefault() ?? items[0].StableKey;
        return new DiscoveredDevice
        {
            DiscoveryId = CreateId(identitySeed),
            DisplayName = displayName,
            HostName = host,
            Addresses = addresses,
            Services = services,
            Sources = sources,
            Metadata = metadata.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string NormalizeHost(string value) => value.Trim().TrimEnd('.');

    private static string CreateId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash[..12]).ToLowerInvariant();
    }
}

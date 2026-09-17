using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

public sealed class EpgProviderResolver
{
    private readonly IReadOnlyDictionary<string, IEpgProvider> providers;

    public EpgProviderResolver(IEnumerable<IEpgProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var all = providers.ToArray();
        var duplicate = all.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Multiple EPG providers are registered for '{duplicate.Key}'.");
        this.providers = all.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IEpgProvider Resolve(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (!providers.TryGetValue(providerId.Trim(), out var provider))
            throw new EpgSourceConfigurationException("No EPG provider is registered for this source.");
        return provider;
    }
}

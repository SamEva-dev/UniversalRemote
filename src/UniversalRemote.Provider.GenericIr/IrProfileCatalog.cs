using System.Collections.Concurrent;

namespace UniversalRemote.Provider.GenericIr;

public interface IIrProfileCatalog
{
    IReadOnlyList<IrProfile> List();
    bool TryGet(string profileId, out IrProfile profile);
    void Upsert(IrProfile profile);
}

public sealed class InMemoryIrProfileCatalog : IIrProfileCatalog
{
    private readonly ConcurrentDictionary<string, IrProfile> profiles = new(StringComparer.Ordinal);

    public InMemoryIrProfileCatalog(IEnumerable<IrProfile>? seed = null)
    {
        if (seed is null) return;
        foreach (var profile in seed) Upsert(profile);
    }

    public IReadOnlyList<IrProfile> List()
        => profiles.Values.OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool TryGet(string profileId, out IrProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return profiles.TryGetValue(profileId, out profile!);
    }

    public void Upsert(IrProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profiles[profile.Id] = profile;
    }
}

public static class BuiltInIrProfiles
{
    private const string BenchResourceName = "UniversalRemote.Provider.GenericIr.Profiles.bench.synthetic.tv.json";
    private static readonly Lazy<IrProfile> bench = new(LoadBench, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Synthetic bench signal only. It intentionally claims no real television compatibility.</summary>
    public static IrProfile BenchSyntheticTv => bench.Value;

    private static IrProfile LoadBench()
    {
        using var stream = typeof(BuiltInIrProfiles).Assembly.GetManifestResourceStream(BenchResourceName)
            ?? throw new InvalidOperationException("Embedded IR bench profile is missing.");
        return IrProfileParser.Parse(stream);
    }
}

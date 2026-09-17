using System.Collections.Concurrent;

namespace UniversalRemote.Remote.Provider.GenericIr;

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

/// <summary>
/// Small local profile store. IR codes are not credentials, but the file format is still bounded and validated before
/// becoming executable. Corrupt files are ignored so one bad import cannot prevent application startup.
/// </summary>
public sealed class FileIrProfileCatalog : IIrProfileCatalog
{
    private readonly object gate = new();
    private readonly Dictionary<string, IrProfile> profiles = new(StringComparer.Ordinal);
    private readonly string directory;

    public FileIrProfileCatalog(string directory, IEnumerable<IrProfile>? seed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
        foreach (var profile in seed ?? []) profiles[profile.Id] = profile;

        foreach (var file in Directory.EnumerateFiles(this.directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var profile = IrProfileParser.Parse(stream);
                profiles[profile.Id] = profile;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                // Invalid local imports are intentionally skipped. The UI can replace them with a valid profile.
            }
        }
    }

    public IReadOnlyList<IrProfile> List()
    {
        lock (gate) return profiles.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool TryGet(string profileId, out IrProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        lock (gate) return profiles.TryGetValue(profileId, out profile!);
    }

    public void Upsert(IrProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        IrProfile.ValidateIdentifier(profile.Id, nameof(profile));
        var json = IrProfileJson.Serialize(profile);
        var target = Path.Combine(directory, profile.Id + ".json");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (gate)
        {
            try
            {
                File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
                File.Move(temporary, target, true);
                profiles[profile.Id] = profile;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            }
        }
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

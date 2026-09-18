namespace UniversalRemote.Media.Abstractions;

/// <summary>Filters a normalized provider catalogue without exposing provider-specific concepts.</summary>
public sealed record MediaCatalogRequest
{
    public IReadOnlySet<MediaItemKind> Kinds { get; }

    public MediaCatalogRequest(IEnumerable<MediaItemKind>? kinds = null)
    {
        var snapshot = (kinds ?? Enum.GetValues<MediaItemKind>()).ToHashSet();
        if (snapshot.Any(x => !Enum.IsDefined(x)))
            throw new ArgumentException("Catalogue request contains an unknown media kind.", nameof(kinds));
        Kinds = snapshot;
    }
}

/// <summary>Normalized snapshot returned by one media provider.</summary>
public sealed record MediaCatalogSnapshot
{
    public Guid SourceId { get; }
    public IReadOnlyList<MediaItem> Items { get; }
    public DateTimeOffset RefreshedAt { get; }

    public MediaCatalogSnapshot(Guid sourceId, IEnumerable<MediaItem> items, DateTimeOffset refreshedAt)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        if (snapshot.Any(x => x is null)) throw new ArgumentException("Media catalogue must not contain null items.", nameof(items));
        if (snapshot.Any(x => x.SourceId != sourceId))
            throw new ArgumentException("Every media item in a catalogue snapshot must belong to the requested source.", nameof(items));

        SourceId = sourceId;
        Items = Array.AsReadOnly(snapshot);
        RefreshedAt = refreshedAt;
    }
}

/// <summary>
/// Provider boundary for normalized media catalogues. Provider implementations must honor cancellation,
/// avoid leaking credentials and return MediaItem objects without embedded stream secrets.
/// </summary>
public interface IMediaProvider
{
    string Id { get; }
    Task<MediaCatalogSnapshot> GetCatalogAsync(
        MediaSource source,
        MediaCatalogRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMediaCatalog
{
    Task<MediaCatalogSnapshot> GetAsync(
        MediaSource source,
        MediaCatalogRequest? request = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Persistence contract for non-secret media source metadata.</summary>
public interface IMediaSourceRepository
{
    Task<MediaSource?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaSource>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaSource> SaveAsync(MediaSource source, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Redacted wrapper for provider credentials. ToString never exposes the underlying secret.
/// </summary>
public sealed class MediaSecret
{
    private readonly string value;

    public MediaSecret(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    public string Reveal() => value;
    public override string ToString() => "[REDACTED]";
}

/// <summary>
/// Secure storage abstraction. Implementations must use platform-secure storage and must not log secret values.
/// </summary>
public interface IMediaCredentialStore
{
    Task<MediaSecret?> GetAsync(string credentialReference, CancellationToken cancellationToken = default);
    Task SetAsync(string credentialReference, MediaSecret secret, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string credentialReference, CancellationToken cancellationToken = default);
}

public enum MediaSourceSetupFieldKind
{
    Text,
    Uri,
    Secret
}

/// <summary>Provider-declared setup field so the UI can configure sources without provider-specific branches.</summary>
public sealed record MediaSourceSetupField
{
    public string Key { get; }
    public string Label { get; }
    public string Placeholder { get; }
    public MediaSourceSetupFieldKind Kind { get; }

    public MediaSourceSetupField(string key, string label, string placeholder, MediaSourceSetupFieldKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Key = key.Trim();
        Label = label.Trim();
        Placeholder = placeholder?.Trim() ?? string.Empty;
        Kind = kind;
    }
}

/// <summary>
/// Provider-owned source setup adapter. It keeps M3U/Xtream validation and credential encoding out of MAUI pages.
/// </summary>
public interface IMediaSourceSetupProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    IReadOnlyList<MediaSourceSetupField> Fields { get; }
    MediaSecret CreateSecret(IReadOnlyDictionary<string, string> values);
}

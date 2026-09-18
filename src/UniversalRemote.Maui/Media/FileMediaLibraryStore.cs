using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

/// <summary>
/// Local, non-secret persistence for Media favorites and playback history. The file never contains stream URLs,
/// source credentials or provider secrets. Credentials remain in IMediaCredentialStore/SecureStorage.
/// </summary>
public sealed class FileMediaLibraryStore : IMediaFavoriteRepository, IMediaHistoryRepository, IDisposable
{
    private const int MaximumFavorites = 500;
    private const int MaximumHistory = 500;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public FileMediaLibraryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    async Task<IReadOnlyList<MediaFavorite>> IMediaFavoriteRepository.ListAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Favorites
            .Select(ToDomain)
            .Where(x => x is not null)
            .Cast<MediaFavorite>()
            .OrderByDescending(x => x.AddedAt)
            .ToArray();
    }

    async Task<MediaFavorite?> IMediaFavoriteRepository.FindAsync(MediaReference reference, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Favorites
            .Where(x => Matches(x.SourceId, x.ExternalId, x.Kind, reference))
            .Select(ToDomain)
            .FirstOrDefault(x => x is not null);
    }

    async Task IMediaFavoriteRepository.SaveAsync(MediaFavorite favorite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        await MutateAsync(document =>
        {
            document.Favorites.RemoveAll(x => Matches(x.SourceId, x.ExternalId, x.Kind, favorite.Reference));
            document.Favorites.Add(ToDto(favorite));
            document.Favorites = document.Favorites.OrderByDescending(x => x.AddedAt).Take(MaximumFavorites).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> IMediaFavoriteRepository.DeleteAsync(MediaReference reference, CancellationToken cancellationToken)
    {
        var removed = false;
        await MutateAsync(document =>
        {
            removed = document.Favorites.RemoveAll(x => Matches(x.SourceId, x.ExternalId, x.Kind, reference)) > 0;
        }, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    async Task<IReadOnlyList<MediaHistoryEntry>> IMediaHistoryRepository.ListAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.History
            .Select(ToDomain)
            .Where(x => x is not null)
            .Cast<MediaHistoryEntry>()
            .OrderByDescending(x => x.LastWatchedAt)
            .ToArray();
    }

    async Task<MediaHistoryEntry?> IMediaHistoryRepository.FindAsync(MediaReference reference, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.History
            .Where(x => Matches(x.SourceId, x.ExternalId, x.Kind, reference))
            .Select(ToDomain)
            .FirstOrDefault(x => x is not null);
    }

    async Task IMediaHistoryRepository.SaveAsync(MediaHistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await MutateAsync(document =>
        {
            document.History.RemoveAll(x => Matches(x.SourceId, x.ExternalId, x.Kind, entry.Reference));
            document.History.Add(ToDto(entry));
            document.History = document.History.OrderByDescending(x => x.LastWatchedAt).Take(MaximumHistory).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> IMediaHistoryRepository.DeleteAsync(MediaReference reference, CancellationToken cancellationToken)
    {
        var removed = false;
        await MutateAsync(document =>
        {
            removed = document.History.RemoveAll(x => Matches(x.SourceId, x.ExternalId, x.Kind, reference)) > 0;
        }, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    private async Task<MediaLibraryDocument> LoadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task MutateAsync(Action<MediaLibraryDocument> mutation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mutation);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            mutation(document);
            await SaveUnlockedAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task<MediaLibraryDocument> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new MediaLibraryDocument();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, MediaLibraryJsonContext.Default.MediaLibraryDocument, cancellationToken).ConfigureAwait(false)
                ?? new MediaLibraryDocument();
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Corrupt local convenience data must not prevent app startup. Start fresh without touching SecureStorage.
            return new MediaLibraryDocument();
        }
    }

    private async Task SaveUnlockedAsync(MediaLibraryDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, document, MediaLibraryJsonContext.Default.MediaLibraryDocument, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static bool Matches(Guid sourceId, string? externalId, int kind, MediaReference reference)
        => sourceId == reference.SourceId &&
           kind == (int)reference.Kind &&
           string.Equals(externalId, reference.ExternalId, StringComparison.Ordinal);

    private static MediaFavoriteDto ToDto(MediaFavorite item) => new()
    {
        SourceId = item.Reference.SourceId,
        ExternalId = item.Reference.ExternalId,
        Kind = (int)item.Reference.Kind,
        Title = item.Title,
        Category = item.Category,
        MinimumAge = item.MinimumAge,
        AddedAt = item.AddedAt
    };

    private static MediaHistoryDto ToDto(MediaHistoryEntry item) => new()
    {
        SourceId = item.Reference.SourceId,
        ExternalId = item.Reference.ExternalId,
        Kind = (int)item.Reference.Kind,
        Title = item.Title,
        PositionTicks = item.Position.Ticks,
        DurationTicks = item.Duration?.Ticks,
        Category = item.Category,
        MinimumAge = item.MinimumAge,
        LastWatchedAt = item.LastWatchedAt,
        Completed = item.Completed
    };

    private static MediaFavorite? ToDomain(MediaFavoriteDto dto)
    {
        if (!TryReference(dto.SourceId, dto.ExternalId, dto.Kind, out var reference) || string.IsNullOrWhiteSpace(dto.Title)) return null;
        try { return new MediaFavorite(reference, dto.Title, dto.Category, dto.AddedAt, dto.MinimumAge); }
        catch { return null; }
    }

    private static MediaHistoryEntry? ToDomain(MediaHistoryDto dto)
    {
        if (!TryReference(dto.SourceId, dto.ExternalId, dto.Kind, out var reference) || string.IsNullOrWhiteSpace(dto.Title)) return null;
        try
        {
            var position = TimeSpan.FromTicks(Math.Max(0, dto.PositionTicks));
            TimeSpan? duration = dto.DurationTicks is long ticks && ticks >= 0 ? TimeSpan.FromTicks(ticks) : null;
            return new MediaHistoryEntry(reference, dto.Title, position, duration, dto.LastWatchedAt, dto.Completed, dto.Category, dto.MinimumAge);
        }
        catch { return null; }
    }

    private static bool TryReference(Guid sourceId, string? externalId, int kind, out MediaReference reference)
    {
        reference = default;
        if (sourceId == Guid.Empty || string.IsNullOrWhiteSpace(externalId) || !Enum.IsDefined(typeof(MediaItemKind), kind)) return false;
        reference = new MediaReference(sourceId, externalId, (MediaItemKind)kind);
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(FileMediaLibraryStore));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gate.Dispose();
    }
}

internal sealed class MediaLibraryDocument
{
    public int Version { get; set; } = 1;
    public List<MediaFavoriteDto> Favorites { get; set; } = [];
    public List<MediaHistoryDto> History { get; set; } = [];
}

internal sealed class MediaFavoriteDto
{
    public Guid SourceId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public int Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int? MinimumAge { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

internal sealed class MediaHistoryDto
{
    public Guid SourceId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public int Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public long PositionTicks { get; set; }
    public long? DurationTicks { get; set; }
    public string? Category { get; set; }
    public int? MinimumAge { get; set; }
    public DateTimeOffset LastWatchedAt { get; set; }
    public bool Completed { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MediaLibraryDocument))]
internal partial class MediaLibraryJsonContext : JsonSerializerContext { }

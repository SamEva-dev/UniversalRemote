using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

/// <summary>
/// Persists only non-secret Media/EPG configuration. Actual playlist endpoints, Xtream credentials and
/// XMLTV endpoints remain behind CredentialReference in IMediaCredentialStore/SecureStorage.
/// </summary>
public sealed class FileMediaConfigurationStore : IMediaSourceRepository, IEpgSourceRepository, IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public FileMediaConfigurationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    async Task<MediaSource?> IMediaSourceRepository.FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return null;
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Sources.Where(x => x.Id == id).Select(ToDomain).FirstOrDefault(x => x is not null);
    }

    async Task<IReadOnlyList<MediaSource>> IMediaSourceRepository.ListAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Sources.Select(ToDomain).Where(x => x is not null).Cast<MediaSource>().OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    async Task<MediaSource> IMediaSourceRepository.SaveAsync(MediaSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        await MutateAsync(document =>
        {
            document.Sources.RemoveAll(x => x.Id == source.Id);
            document.Sources.Add(ToDto(source));
        }, cancellationToken).ConfigureAwait(false);
        return source;
    }

    async Task<bool> IMediaSourceRepository.DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return false;
        var removed = false;
        await MutateAsync(document =>
        {
            removed = document.Sources.RemoveAll(x => x.Id == id) > 0;
            document.EpgSources.RemoveAll(x => x.MediaSourceId == id);
        }, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    async Task<EpgSource?> IEpgSourceRepository.FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return null;
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.EpgSources.Where(x => x.Id == id).Select(ToDomain).FirstOrDefault(x => x is not null);
    }

    async Task<IReadOnlyList<EpgSource>> IEpgSourceRepository.ListForMediaSourceAsync(Guid mediaSourceId, CancellationToken cancellationToken)
    {
        if (mediaSourceId == Guid.Empty) return [];
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.EpgSources
            .Where(x => x.MediaSourceId == mediaSourceId)
            .Select(ToDomain)
            .Where(x => x is not null)
            .Cast<EpgSource>()
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    async Task<EpgSource> IEpgSourceRepository.SaveAsync(EpgSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        await MutateAsync(document =>
        {
            document.EpgSources.RemoveAll(x => x.Id == source.Id);
            document.EpgSources.Add(ToDto(source));
        }, cancellationToken).ConfigureAwait(false);
        return source;
    }

    async Task<bool> IEpgSourceRepository.DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return false;
        var removed = false;
        await MutateAsync(document => removed = document.EpgSources.RemoveAll(x => x.Id == id) > 0, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    private async Task<MediaConfigurationDocument> LoadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task MutateAsync(Action<MediaConfigurationDocument> mutation, CancellationToken cancellationToken)
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

    private async Task<MediaConfigurationDocument> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new MediaConfigurationDocument();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, MediaConfigurationJsonContext.Default.MediaConfigurationDocument, cancellationToken).ConfigureAwait(false)
                ?? new MediaConfigurationDocument();
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Corrupt configuration must not crash startup. Secrets remain untouched in SecureStorage.
            return new MediaConfigurationDocument();
        }
    }

    private async Task SaveUnlockedAsync(MediaConfigurationDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, document, MediaConfigurationJsonContext.Default.MediaConfigurationDocument, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static MediaSourceDto ToDto(MediaSource source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        ProviderId = source.ProviderId,
        CredentialReference = source.CredentialReference,
        IsEnabled = source.IsEnabled
    };

    private static EpgSourceDto ToDto(EpgSource source) => new()
    {
        Id = source.Id,
        MediaSourceId = source.MediaSourceId,
        DisplayName = source.DisplayName,
        ProviderId = source.ProviderId,
        CredentialReference = source.CredentialReference,
        IsEnabled = source.IsEnabled
    };

    private static MediaSource? ToDomain(MediaSourceDto dto)
    {
        try { return new MediaSource(dto.Id, dto.DisplayName, dto.ProviderId, dto.CredentialReference, dto.IsEnabled); }
        catch { return null; }
    }

    private static EpgSource? ToDomain(EpgSourceDto dto)
    {
        try { return new EpgSource(dto.Id, dto.MediaSourceId, dto.DisplayName, dto.ProviderId, dto.CredentialReference, dto.IsEnabled); }
        catch { return null; }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(FileMediaConfigurationStore));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gate.Dispose();
    }
}

internal sealed class MediaConfigurationDocument
{
    public int Version { get; set; } = 1;
    public List<MediaSourceDto> Sources { get; set; } = [];
    public List<EpgSourceDto> EpgSources { get; set; } = [];
}

internal sealed class MediaSourceDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string? CredentialReference { get; set; }
    public bool IsEnabled { get; set; } = true;
}

internal sealed class EpgSourceDto
{
    public Guid Id { get; set; }
    public Guid MediaSourceId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string CredentialReference { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

[JsonSerializable(typeof(MediaConfigurationDocument))]
internal partial class MediaConfigurationJsonContext : JsonSerializerContext { }

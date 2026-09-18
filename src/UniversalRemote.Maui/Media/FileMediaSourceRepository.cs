using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

/// <summary>
/// Persists only non-secret MediaSource metadata. Server credentials and playlist URLs remain in SecureStorage.
/// </summary>
public sealed class FileMediaSourceRepository : IMediaSourceRepository, IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public FileMediaSourceRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public async Task<MediaSource?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => (await ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id);

    public async Task<IReadOnlyList<MediaSource>> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            return document.Sources.Select(ToDomain).Where(x => x is not null).Cast<MediaSource>()
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<MediaSource> SaveAsync(MediaSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            document.Sources.RemoveAll(x => x.Id == source.Id);
            document.Sources.Add(new MediaSourceDto
            {
                Id = source.Id,
                DisplayName = source.DisplayName,
                ProviderId = source.ProviderId,
                CredentialReference = source.CredentialReference,
                IsEnabled = source.IsEnabled
            });
            await SaveUnlockedAsync(document, cancellationToken).ConfigureAwait(false);
            return source;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return false;
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var removed = document.Sources.RemoveAll(x => x.Id == id) > 0;
            if (removed) await SaveUnlockedAsync(document, cancellationToken).ConfigureAwait(false);
            return removed;
        }
        finally { gate.Release(); }
    }

    private async Task<MediaSourceDocument> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new MediaSourceDocument();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, MediaSourceJsonContext.Default.MediaSourceDocument, cancellationToken).ConfigureAwait(false)
                ?? new MediaSourceDocument();
        }
        catch (OperationCanceledException) { throw; }
        catch { return new MediaSourceDocument(); }
    }

    private async Task SaveUnlockedAsync(MediaSourceDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, document, MediaSourceJsonContext.Default.MediaSourceDocument, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static MediaSource? ToDomain(MediaSourceDto dto)
    {
        try
        {
            if (dto.Id == Guid.Empty || string.IsNullOrWhiteSpace(dto.DisplayName) || string.IsNullOrWhiteSpace(dto.ProviderId)) return null;
            return new MediaSource(dto.Id, dto.DisplayName, dto.ProviderId, dto.CredentialReference, dto.IsEnabled);
        }
        catch { return null; }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(FileMediaSourceRepository));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gate.Dispose();
    }
}

internal sealed class MediaSourceDocument
{
    public int Version { get; set; } = 1;
    public List<MediaSourceDto> Sources { get; set; } = [];
}

internal sealed class MediaSourceDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string? CredentialReference { get; set; }
    public bool IsEnabled { get; set; } = true;
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MediaSourceDocument))]
internal partial class MediaSourceJsonContext : JsonSerializerContext;

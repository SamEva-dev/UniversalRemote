using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalRemote.Abstractions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Activities;

/// <summary>
/// Small local JSON repository for media scenarios. The document contains only normalized IDs and display metadata;
/// stream URLs and provider credentials are structurally impossible to persist here.
/// </summary>
public sealed class FileMediaActivityRepository : IMediaActivityRepository, IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public FileMediaActivityRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public async Task<MediaActivityDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return null;
        var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(x => x.Id == id);
    }

    public async Task<IReadOnlyList<MediaActivityDefinition>> ListAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public async Task<MediaActivityDefinition> SaveAsync(MediaActivityDefinition activity, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(activity);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var index = document.Items.FindIndex(x => x.Id == activity.Id);
            var dto = Entry.From(activity);
            if (index >= 0) document.Items[index] = dto;
            else document.Items.Add(dto);
            await WriteDocumentUnsafeAsync(document, cancellationToken).ConfigureAwait(false);
            return activity;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (id == Guid.Empty) return false;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var removed = document.Items.RemoveAll(x => x.Id == id) > 0;
            if (removed) await WriteDocumentUnsafeAsync(document, cancellationToken).ConfigureAwait(false);
            return removed;
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<MediaActivityDefinition>> LoadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<MediaActivityDefinition>(document.Items.Count);
            foreach (var item in document.Items)
            {
                try { result.Add(item.ToDomain()); }
                catch (ArgumentException) { /* Ignore one malformed local entry rather than losing the complete file. */ }
            }
            return result;
        }
        finally { gate.Release(); }
    }

    private async Task<Document> ReadDocumentUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new Document();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, MediaActivityJsonContext.Default.Document, cancellationToken).ConfigureAwait(false)
                ?? new Document();
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return new Document(); }
        catch (JsonException) { return new Document(); }
    }

    private async Task WriteDocumentUnsafeAsync(Document document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, document, MediaActivityJsonContext.Default.Document, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gate.Dispose();
    }

    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<Entry> Items { get; set; } = [];
    }

    internal sealed class Entry
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? PreparationActivityId { get; set; }
        public Guid SourceId { get; set; }
        public string ExternalId { get; set; } = string.Empty;
        public MediaItemKind MediaKind { get; set; }
        public string MediaTitle { get; set; } = string.Empty;
        public MediaActivityTargetMode TargetMode { get; set; }
        public string? TargetId { get; set; }
        public ActivityFailurePolicy PreparationFailurePolicy { get; set; }

        public static Entry From(MediaActivityDefinition value) => new()
        {
            Id = value.Id,
            Name = value.Name,
            PreparationActivityId = value.PreparationActivityId,
            SourceId = value.Media.SourceId,
            ExternalId = value.Media.ExternalId,
            MediaKind = value.Media.Kind,
            MediaTitle = value.MediaTitle,
            TargetMode = value.TargetMode,
            TargetId = value.TargetId,
            PreparationFailurePolicy = value.PreparationFailurePolicy
        };

        public MediaActivityDefinition ToDomain() => new(
            Id,
            Name,
            PreparationActivityId,
            new MediaReference(SourceId, ExternalId, MediaKind),
            MediaTitle,
            TargetMode,
            TargetId,
            PreparationFailurePolicy);
    }
}

[JsonSerializable(typeof(FileMediaActivityRepository.Document))]
internal sealed partial class MediaActivityJsonContext : JsonSerializerContext
{
}

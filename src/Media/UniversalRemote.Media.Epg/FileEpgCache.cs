using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

/// <summary>
/// Local JSON cache for normalized guide rows. It never stores XMLTV endpoint credentials or stream URLs.
/// </summary>
public sealed class FileEpgCache : IEpgCache
{
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);

    public FileEpgCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory.Trim());
        Directory.CreateDirectory(this.directory);
    }

    public async Task<EpgGuideSnapshot?> GetAsync(Guid mediaSourceId, Guid epgSourceId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default)
    {
        var path = PathFor(mediaSourceId, epgSourceId, windowStart, windowEnd);
        if (!File.Exists(path)) return null;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(path);
            var dto = await JsonSerializer.DeserializeAsync(stream, EpgCacheJsonContext.Default.EpgCacheDto, cancellationToken).ConfigureAwait(false);
            return dto?.ToModel();
        }
        catch (JsonException)
        {
            TryDelete(path);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetAsync(EpgGuideSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var path = PathFor(snapshot.MediaSourceId, snapshot.EpgSourceId, snapshot.WindowStart, snapshot.WindowEnd);
        var temp = path + ".tmp";
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, EpgCacheDto.FromModel(snapshot), EpgCacheJsonContext.Default.EpgCacheDto, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, true);
        }
        finally
        {
            TryDelete(temp);
            gate.Release();
        }
    }

    public async Task ClearAsync(Guid mediaSourceId, Guid epgSourceId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var prefix = Prefix(mediaSourceId, epgSourceId);
            foreach (var file in Directory.EnumerateFiles(directory, prefix + "*.json")) TryDelete(file);
        }
        finally
        {
            gate.Release();
        }
    }

    private string PathFor(Guid mediaSourceId, Guid epgSourceId, DateTimeOffset start, DateTimeOffset end)
    {
        if (mediaSourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(mediaSourceId));
        if (epgSourceId == Guid.Empty) throw new ArgumentException("EPG source ID must not be empty.", nameof(epgSourceId));
        var window = $"{start.UtcDateTime:yyyyMMddHHmm}-{end.UtcDateTime:yyyyMMddHHmm}";
        return Path.Combine(directory, Prefix(mediaSourceId, epgSourceId) + window + ".json");
    }

    private static string Prefix(Guid mediaSourceId, Guid epgSourceId)
    {
        var identity = $"{mediaSourceId:N}|{epgSourceId:N}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return "epg-" + hash[..24] + "-";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

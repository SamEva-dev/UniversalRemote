using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Storage;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

/// <summary>
/// Persists only resume positions. Preference keys are hashed so provider identifiers never become platform
/// preference names; no stream URI or credential is stored.
/// </summary>
public sealed class PreferencesPlaybackCheckpointStore : IPlaybackCheckpointStore
{
    private const string Prefix = "media.resume.v1.";

    public Task<TimeSpan?> LoadAsync(Guid sourceId, string mediaExternalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(sourceId, mediaExternalId);
        var ticks = Preferences.Default.Get(key, -1L);
        return Task.FromResult<TimeSpan?>(ticks >= 0 ? TimeSpan.FromTicks(ticks) : null);
    }

    public Task SaveAsync(Guid sourceId, string mediaExternalId, TimeSpan position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        Preferences.Default.Set(Key(sourceId, mediaExternalId), position.Ticks);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid sourceId, string mediaExternalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Preferences.Default.Remove(Key(sourceId, mediaExternalId));
        return Task.CompletedTask;
    }

    private static string Key(Guid sourceId, string mediaExternalId)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaExternalId);
        var identity = $"{sourceId:N}|{mediaExternalId.Trim()}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Prefix + hash;
    }
}

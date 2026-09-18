using Microsoft.Maui.Storage;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

public sealed class SecureMediaCredentialStore : IMediaCredentialStore
{
    public async Task<MediaSecret?> GetAsync(string credentialReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await SecureStorage.Default.GetAsync(Key(credentialReference)).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) ? null : new MediaSecret(value);
    }

    public Task SetAsync(string credentialReference, MediaSecret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(Key(credentialReference), secret.Reveal());
    }

    public Task<bool> DeleteAsync(string credentialReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SecureStorage.Default.Remove(Key(credentialReference)));
    }

    private static string Key(string credentialReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);
        return $"universalremote.media.{credentialReference}";
    }
}

using System.Text.Json;
using UniversalRemote.Provider.AndroidTv;

namespace UniversalRemote.Maui.AndroidTv;

public sealed class SecureAndroidTvCredentialStore : IAndroidTvCredentialStore
{
    private static string Key(string host) => "universalremote.androidtv." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(host)));

    public async Task<AndroidTvCredentials?> GetAsync(string host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await SecureStorage.Default.GetAsync(Key(host)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AndroidTvCredentials>(json);
    }

    public async Task SaveAsync(AndroidTvCredentials credentials, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(Key(credentials.Host), JsonSerializer.Serialize(credentials)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

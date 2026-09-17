using Microsoft.Maui.Storage;
using System.Text.Json;
using UniversalRemote.Maui.Storage;
using UniversalRemote.Remote.Provider.Samsung;
namespace UniversalRemote.Maui.Samsung;
public sealed class SecureSamsungCredentialStore : ISamsungCredentialStore
{
    private static string Key(string host) => $"universalremote.samsung.{host}";
    public async Task<SamsungCredentials?> GetAsync(string host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await SecureStorage.Default.GetAsync(Key(host)).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, CredentialJsonContext.Default.SamsungCredentials);
    }
    public Task SaveAsync(SamsungCredentials credentials, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(Key(credentials.Host), JsonSerializer.Serialize(credentials, CredentialJsonContext.Default.SamsungCredentials));
    }
}

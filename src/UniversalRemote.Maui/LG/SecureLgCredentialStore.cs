using Microsoft.Maui.Storage;
using System.Text.Json;
using UniversalRemote.Maui.Storage;
using UniversalRemote.Remote.Provider.LG;
namespace UniversalRemote.Maui.LG;
public sealed class SecureLgCredentialStore : ILgCredentialStore
{
    private static string Key(string host) => $"universalremote.lg.{host}";
    public async Task<LgCredentials?> GetAsync(string host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await SecureStorage.Default.GetAsync(Key(host)).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, CredentialJsonContext.Default.LgCredentials);
    }
    public Task SaveAsync(LgCredentials credentials, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(Key(credentials.Host), JsonSerializer.Serialize(credentials, CredentialJsonContext.Default.LgCredentials));
    }
}

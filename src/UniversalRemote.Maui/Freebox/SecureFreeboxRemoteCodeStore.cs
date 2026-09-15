using Microsoft.Maui.Storage;
using UniversalRemote.Provider.Freebox;
namespace UniversalRemote.Maui.Freebox;

public sealed class SecureFreeboxRemoteCodeStore : IFreeboxRemoteCodeStore
{
    private static string Key(string deviceKey) => $"freebox.remote.{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(deviceKey))).ToLowerInvariant()}";
    public Task<string?> GetAsync(string deviceKey, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return SecureStorage.Default.GetAsync(Key(deviceKey)); }
    public async Task SaveAsync(string deviceKey, string code, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); await SecureStorage.Default.SetAsync(Key(deviceKey), code).ConfigureAwait(false); }
}

using Microsoft.Maui.Storage;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Maui.Hub;

public sealed class SecureInfraredHubSelectionStore : IInfraredHubSelectionStore
{
    private const string Key = "universalremote.hub.selected.v1";

    public async ValueTask<InfraredHubSelection?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await SecureStorage.Default.GetAsync(Key).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split('|', 2, StringSplitOptions.None);
        if (parts.Length != 2 || !Enum.TryParse<InfraredHubTransportKind>(parts[0], out var kind)) return null;
        try { return new InfraredHubSelection(new InfraredHubId(parts[1]), kind); }
        catch (ArgumentException) { return null; }
    }

    public async Task SaveAsync(InfraredHubSelection selection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(Key, $"{selection.TransportKind}|{selection.HubId.Value}").ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}

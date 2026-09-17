using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub;

/// <summary>Resolves a persisted hub choice without exposing Wi-Fi/BLE endpoint details.</summary>
public sealed class InfraredHubSelectionAccessor(
    IInfraredHubSelectionStore selectionStore,
    IEnumerable<IInfraredHubTransport> transports) : IInfraredHubSelectionAccessor
{
    private readonly IReadOnlyList<IInfraredHubTransport> _transports = transports?.ToArray()
        ?? throw new ArgumentNullException(nameof(transports));

    public async ValueTask<IInfraredHub?> GetSelectedHubAsync(CancellationToken cancellationToken = default)
    {
        var selection = await selectionStore.GetAsync(cancellationToken).ConfigureAwait(false);
        if (selection is null) return null;
        var transport = _transports.FirstOrDefault(x => x.Kind == selection.Value.TransportKind);
        return transport is null ? null : new InfraredHubClient(selection.Value.HubId, transport);
    }
}

/// <summary>Desktop/test fallback. Platform applications may replace it with secure persistence.</summary>
public sealed class InMemoryInfraredHubSelectionStore : IInfraredHubSelectionStore
{
    private readonly object _gate = new();
    private InfraredHubSelection? _selection;

    public ValueTask<InfraredHubSelection?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return ValueTask.FromResult(_selection);
    }

    public Task SaveAsync(InfraredHubSelection selection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _selection = selection;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _selection = null;
        return Task.CompletedTask;
    }
}

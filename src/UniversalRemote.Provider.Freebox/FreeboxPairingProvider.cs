using System.Collections.Concurrent;
using UniversalRemote.Abstractions;
namespace UniversalRemote.Provider.Freebox;

public sealed class FreeboxPairingProvider(IFreeboxRemoteCodeStore codeStore) : IDevicePairingProvider
{
    private readonly ConcurrentDictionary<Guid, PairingCandidate> pending = new();
    public string Id => FreeboxRemoteProvider.ProviderId;
    public PairingCandidate? Match(PairingProbe probe)
    {
        var isFreebox = probe.Services.Any(x => x.Contains("_fbx-api._tcp", StringComparison.OrdinalIgnoreCase)) || probe.DisplayName.Contains("Freebox", StringComparison.OrdinalIgnoreCase);
        return isFreebox ? new PairingCandidate(Id, "hd1.freebox.fr", $"{probe.DisplayName} — Player Révolution") : null;
    }
    public Task<PairingChallenge> StartAsync(PairingCandidate candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid(); pending[id] = candidate;
        return Task.FromResult(new PairingChallenge(id, Id, candidate.DeviceKey, candidate.DisplayName,
            "Saisis le code télécommande réseau affiché dans les informations du Freebox Player Révolution.", 8));
    }
    public async Task<PairingCompletion> CompleteAsync(Guid challengeId, string code, CancellationToken cancellationToken)
    {
        if (!pending.TryRemove(challengeId, out var candidate)) throw new InvalidOperationException("Freebox pairing challenge is unknown or expired.");
        if (code.Length is < 4 or > 12 || code.Any(c => !char.IsDigit(c))) throw new InvalidDataException("Freebox remote code must contain digits only.");
        await codeStore.SaveAsync(candidate.DeviceKey, code, cancellationToken).ConfigureAwait(false);
        return new PairingCompletion(new DeviceRoute(Id, candidate.DeviceKey, FreeboxRemoteProvider.Capabilities), candidate.DisplayName);
    }
}

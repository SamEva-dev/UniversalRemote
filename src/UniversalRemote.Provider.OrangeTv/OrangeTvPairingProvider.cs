using System.Collections.Concurrent;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Provider.OrangeTv;

public sealed class OrangeTvPairingProvider(HttpClient httpClient) : IDevicePairingProvider, IManualPairingProvider
{
    private sealed record Pending(string Address, string DisplayName);
    private readonly ConcurrentDictionary<Guid, Pending> pending = new();

    public string Id => OrangeTvRemoteProvider.ProviderId;

    public PairingCandidate? Match(PairingProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (!LooksLikeOrangeCandidate(probe.DisplayName, probe.Services, probe.Metadata)) return null;
        var address = probe.Addresses.Select(static x => x?.Trim()).FirstOrDefault(x => OrangeTvEndpoint.TryNormalizeLocalAddress(x, out _));
        return address is null ? null : CreateManualCandidate(address, probe.DisplayName);
    }

    public PairingCandidate? CreateManualCandidate(string deviceKey, string? displayName = null)
    {
        if (!OrangeTvEndpoint.TryNormalizeLocalAddress(deviceKey, out var normalized)) return null;
        var name = string.IsNullOrWhiteSpace(displayName) ? "Décodeur TV Orange" : displayName.Trim();
        return new PairingCandidate(Id, normalized, name);
    }

    public Task<PairingChallenge> StartAsync(PairingCandidate candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(candidate.ProviderId, Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Requested pairing provider is unavailable.");
        if (!OrangeTvEndpoint.TryNormalizeLocalAddress(candidate.DeviceKey, out var address))
            throw new InvalidOperationException("The device address is not an allowed local endpoint.");

        var id = Guid.NewGuid();
        pending[id] = new Pending(address, candidate.DisplayName);
        return Task.FromResult(new PairingChallenge(
            id,
            Id,
            address,
            candidate.DisplayName,
            "UniversalRemote va vérifier localement le décodeur Orange sur le port 8080. Aucun code ni mot de passe n'est envoyé.",
            0));
    }

    public async Task<PairingCompletion> CompleteAsync(Guid challengeId, string code, CancellationToken cancellationToken)
    {
        if (!pending.TryRemove(challengeId, out var item))
            throw new InvalidOperationException("Pairing challenge is no longer available.");
        if (!string.IsNullOrEmpty(code))
            throw new InvalidOperationException("Orange TV pairing does not accept a code.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, OrangeTvEndpoint.Status(item.Address));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var status = await OrangeTvProtocol.ReadStatusAsync(response, cancellationToken).ConfigureAwait(false);
            if (status is null || !OrangeTvProtocol.LooksLikeOrangeTv(status))
                throw new InvalidOperationException("The local endpoint did not identify a supported Orange TV decoder.");

            var displayName = string.IsNullOrWhiteSpace(status.FriendlyName) ? item.DisplayName : status.FriendlyName.Trim();
            return new PairingCompletion(OrangeTvRemoteProvider.CreateRoute(item.Address), displayName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { throw new InvalidOperationException("The Orange TV endpoint did not answer in time."); }
        catch (HttpRequestException) { throw new InvalidOperationException("The Orange TV endpoint is unavailable on the local network."); }
    }

    private static bool LooksLikeOrangeCandidate(
        string displayName,
        IReadOnlyList<string> services,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (ContainsOrangeTvText(displayName)) return true;
        if (services.Any(ContainsOrangeTvText)) return true;
        return metadata is not null && metadata.Any(x => ContainsOrangeTvText(x.Key) || ContainsOrangeTvText(x.Value));
    }

    private static bool ContainsOrangeTvText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Contains("orange", StringComparison.OrdinalIgnoreCase)
            || value.Contains("decodeur tv", StringComparison.OrdinalIgnoreCase)
            || value.Contains("décodeur tv", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tv uhd", StringComparison.OrdinalIgnoreCase);
    }
}

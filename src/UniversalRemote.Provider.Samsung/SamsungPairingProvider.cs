using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Provider.Samsung;

public sealed class SamsungPairingProvider(ISamsungCredentialStore store) : IDevicePairingProvider
{
    public const string ProviderId = "samsung-tizen";
    private readonly ConcurrentDictionary<Guid, PendingPairing> pending = new();
    public string Id => ProviderId;

    public PairingCandidate? Match(PairingProbe probe)
    {
        var haystack = string.Join(" ", new[] { probe.DisplayName }.Concat(probe.Services).Concat(probe.Metadata?.Values ?? Array.Empty<string>()));
        if (!haystack.Contains("samsung", StringComparison.OrdinalIgnoreCase) &&
            !haystack.Contains("RemoteControlReceiver", StringComparison.OrdinalIgnoreCase)) return null;
        var host = probe.Addresses.FirstOrDefault();
        return string.IsNullOrWhiteSpace(host) ? null : new PairingCandidate(Id, host, probe.DisplayName);
    }

    public async Task<PairingChallenge> StartAsync(PairingCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.ProviderId != Id) throw new InvalidOperationException("Pairing candidate does not belong to Samsung TV.");
        string? fingerprint = null;
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            fingerprint = SamsungProtocol.Fingerprint(certificate);
            return certificate is not null;
        };
        await socket.ConnectAsync(SamsungProtocol.BuildUri(candidate.DeviceKey), cancellationToken).ConfigureAwait(false);

        string? token = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (token is null)
            token = SamsungProtocol.TryGetToken(await SamsungProtocol.ReceiveTextAsync(socket, timeout.Token).ConfigureAwait(false));

        if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("Samsung TV did not issue a pairing token.");
        var id = Guid.NewGuid();
        pending[id] = new PendingPairing(candidate, token, fingerprint, DateTimeOffset.UtcNow);
        return new PairingChallenge(id, Id, candidate.DeviceKey, candidate.DisplayName,
            "Accepte UniversalRemote sur l’écran du téléviseur Samsung puis continue.", 0);
    }

    public async Task<PairingCompletion> CompleteAsync(Guid challengeId, string code, CancellationToken cancellationToken)
    {
        if (!pending.TryRemove(challengeId, out var item)) throw new InvalidOperationException("Pairing challenge is unknown or expired.");
        if (DateTimeOffset.UtcNow - item.CreatedAt > TimeSpan.FromMinutes(3)) throw new TimeoutException("Pairing challenge expired.");
        await store.SaveAsync(new SamsungCredentials(item.Candidate.DeviceKey, item.Token, item.Fingerprint), cancellationToken).ConfigureAwait(false);
        return new PairingCompletion(new DeviceRoute(Id, item.Candidate.DeviceKey, SamsungRemoteProvider.Capabilities), item.Candidate.DisplayName);
    }

    private sealed record PendingPairing(PairingCandidate Candidate, string Token, string? Fingerprint, DateTimeOffset CreatedAt);
}

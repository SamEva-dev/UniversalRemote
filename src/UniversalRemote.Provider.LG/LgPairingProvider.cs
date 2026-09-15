using System.Collections.Concurrent;
using System.Net.WebSockets;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.LG;

public sealed class LgPairingProvider(ILgCredentialStore store) : IDevicePairingProvider
{
    public const string ProviderId = "lg-webos";
    private readonly ConcurrentDictionary<Guid, Pending> pending = new();
    public string Id => ProviderId;
    public PairingCandidate? Match(PairingProbe probe)
    {
        var haystack = string.Join(" ", new[] { probe.DisplayName }.Concat(probe.Services).Concat(probe.Metadata?.Values ?? Array.Empty<string>()));
        if (!haystack.Contains("webos", StringComparison.OrdinalIgnoreCase) &&
            !haystack.Contains("lge-com", StringComparison.OrdinalIgnoreCase) &&
            !haystack.Contains("LG Electronics", StringComparison.OrdinalIgnoreCase)) return null;
        var host = probe.Addresses.FirstOrDefault();
        return string.IsNullOrWhiteSpace(host) ? null : new PairingCandidate(Id, host, probe.DisplayName);
    }

    public async Task<PairingChallenge> StartAsync(PairingCandidate candidate, CancellationToken cancellationToken)
    {
        string? fingerprint = null;
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => { fingerprint = LgProtocol.Fingerprint(cert); return cert is not null; };
        await socket.ConnectAsync(LgProtocol.UriFor(candidate.DeviceKey), cancellationToken).ConfigureAwait(false);
        var register = System.Text.Encoding.UTF8.GetBytes(LgProtocol.Register());
        await socket.SendAsync(register, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        string? clientKey = null;
        while (clientKey is null) clientKey = LgProtocol.TryClientKey(await LgProtocol.ReceiveTextAsync(socket, timeout.Token).ConfigureAwait(false));
        var id = Guid.NewGuid(); pending[id] = new Pending(candidate, clientKey, fingerprint, DateTimeOffset.UtcNow);
        return new PairingChallenge(id, Id, candidate.DeviceKey, candidate.DisplayName, "Accepte la demande de connexion UniversalRemote sur le téléviseur LG puis continue.", 0);
    }

    public async Task<PairingCompletion> CompleteAsync(Guid challengeId, string code, CancellationToken cancellationToken)
    {
        if (!pending.TryRemove(challengeId, out var item)) throw new InvalidOperationException("Pairing challenge is unknown or expired.");
        if (DateTimeOffset.UtcNow - item.CreatedAt > TimeSpan.FromMinutes(3)) throw new TimeoutException("Pairing challenge expired.");
        await store.SaveAsync(new LgCredentials(item.Candidate.DeviceKey, item.ClientKey, item.Fingerprint), cancellationToken).ConfigureAwait(false);
        return new PairingCompletion(new DeviceRoute(Id, item.Candidate.DeviceKey, LgRemoteProvider.Capabilities), item.Candidate.DisplayName);
    }
    private sealed record Pending(PairingCandidate Candidate, string ClientKey, string? Fingerprint, DateTimeOffset CreatedAt);
}

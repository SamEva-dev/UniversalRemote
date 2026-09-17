using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Provider.AndroidTv;

public sealed class AndroidTvPairingProvider(IAndroidTvCredentialStore credentialStore) : IDevicePairingProvider, IManualPairingProvider, IAsyncDisposable
{
    public const string ProviderId = "androidtv";
    private readonly ConcurrentDictionary<Guid, PendingPairing> pending = new();
    public string Id => ProviderId;

    public PairingCandidate? Match(PairingProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (!probe.Services.Any(x => x.Contains(AndroidTvProtocol.ServiceType, StringComparison.OrdinalIgnoreCase))) return null;
        var host = probe.Addresses.FirstOrDefault(x => TryNormalizeLocalAddress(x, out _)) ?? string.Empty;
        return string.IsNullOrWhiteSpace(host) ? null : CreateManualCandidate(host, probe.DisplayName);
    }

    public PairingCandidate? CreateManualCandidate(string deviceKey, string? displayName = null)
    {
        if (!TryNormalizeLocalAddress(deviceKey, out var normalized)) return null;
        var name = string.IsNullOrWhiteSpace(displayName) ? "Android TV / Google TV" : displayName.Trim();
        return new PairingCandidate(Id, normalized, name);
    }

    public async Task<PairingChallenge> StartAsync(PairingCandidate candidate, CancellationToken cancellationToken)
    {
        if (!string.Equals(candidate.ProviderId, Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Pairing candidate does not belong to Android TV.");
        if (!TryNormalizeLocalAddress(candidate.DeviceKey, out var normalizedAddress))
            throw new InvalidOperationException("Android TV pairing requires a private local IP address.");
        candidate = candidate with { DeviceKey = normalizedAddress };

        foreach (var item in pending.ToArray())
            if (DateTimeOffset.UtcNow - item.Value.CreatedAt > TimeSpan.FromMinutes(3)
                && pending.TryRemove(item.Key, out var expired))
                await expired.DisposeAsync().ConfigureAwait(false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        var clientCertificate = AndroidTvProtocol.CreateClientCertificate(out var password);
        X509Certificate2? serverCertificate = null;
        var tcp = new TcpClient();
        SslStream? ssl = null;
        var writeGate = new SemaphoreSlim(1, 1);
        var retained = false;
        try
        {
            await tcp.ConnectAsync(candidate.DeviceKey, AndroidTvProtocol.PairingPort, ct).ConfigureAwait(false);
            ssl = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) =>
            {
                serverCertificate?.Dispose();
                serverCertificate = certificate is null ? null : new X509Certificate2(certificate);
                return certificate is not null;
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = candidate.DeviceKey,
                ClientCertificates = new X509CertificateCollection { clientCertificate },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct).ConfigureAwait(false);
            await AndroidTvProtocol.WriteFrameAsync(ssl, AndroidTvProtocol.PairingRequest("UniversalRemote", "UniversalRemote"), writeGate, ct).ConfigureAwait(false);
            Expect(await AndroidTvProtocol.ReadFrameAsync(ssl, ct).ConfigureAwait(false), 11, "pairing request acknowledgement");
            await AndroidTvProtocol.WriteFrameAsync(ssl, AndroidTvProtocol.PairingOptions(), writeGate, ct).ConfigureAwait(false);
            Expect(await AndroidTvProtocol.ReadFrameAsync(ssl, ct).ConfigureAwait(false), 20, "pairing options");
            await AndroidTvProtocol.WriteFrameAsync(ssl, AndroidTvProtocol.PairingConfiguration(), writeGate, ct).ConfigureAwait(false);
            Expect(await AndroidTvProtocol.ReadFrameAsync(ssl, ct).ConfigureAwait(false), 31, "pairing configuration acknowledgement");
            if (serverCertificate is null) throw new AuthenticationException("Android TV did not provide a pairing certificate.");
            var id = Guid.NewGuid();
            pending[id] = new PendingPairing(candidate, tcp, ssl, writeGate, clientCertificate, serverCertificate, password, DateTimeOffset.UtcNow);
            retained = true;
            return new PairingChallenge(id, Id, candidate.DeviceKey, candidate.DisplayName,
                "Saisis le code hexadécimal à 6 caractères affiché sur Android TV / Google TV.", 6);
        }
        finally
        {
            if (!retained)
            {
                ssl?.Dispose(); tcp.Dispose(); writeGate.Dispose();
                clientCertificate.Dispose(); serverCertificate?.Dispose();
            }
        }
    }

    public async Task<PairingCompletion> CompleteAsync(Guid challengeId, string code, CancellationToken cancellationToken)
    {
        if (!pending.TryRemove(challengeId, out var session)) throw new InvalidOperationException("Pairing challenge is unknown or expired.");
        await using var _ = session.ConfigureAwait(false);
        if (DateTimeOffset.UtcNow - session.CreatedAt > TimeSpan.FromMinutes(3)) throw new TimeoutException("Pairing challenge expired.");

        var secret = AndroidTvProtocol.ComputePairingSecret(session.ClientCertificate, session.ServerCertificate, code);
        await AndroidTvProtocol.WriteFrameAsync(session.Stream, AndroidTvProtocol.PairingSecret(secret), session.WriteGate, cancellationToken).ConfigureAwait(false);
        Expect(await AndroidTvProtocol.ReadFrameAsync(session.Stream, cancellationToken).ConfigureAwait(false), 41, "pairing secret acknowledgement");

        var pfx = session.ClientCertificate.Export(X509ContentType.Pkcs12, session.Password);
        var serverHash = Convert.ToHexString(SHA256.HashData(session.ServerCertificate.RawData));
        await credentialStore.SaveAsync(new AndroidTvCredentials(
            session.Candidate.DeviceKey,
            Convert.ToBase64String(pfx),
            session.Password,
            serverHash), cancellationToken).ConfigureAwait(false);

        return new PairingCompletion(
            new DeviceRoute(Id, session.Candidate.DeviceKey, AndroidTvRemoteProvider.Capabilities),
            session.Candidate.DisplayName);
    }

    private static bool TryNormalizeLocalAddress(string? value, out string address)
    {
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!IPAddress.TryParse(value.Trim(), out var ip)) return false;
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        var allowed = ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateIpv4(ip),
            AddressFamily.InterNetworkV6 => ip.IsIPv6LinkLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC,
            _ => false
        };
        if (!allowed) return false;

        address = ip.ToString();
        return true;
    }

    private static bool IsPrivateIpv4(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static void Expect(byte[] message, int field, string stage)
    {
        if (!AndroidTvProtocol.HasField(message, field)) throw new InvalidDataException($"Unexpected Android TV {stage} response.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in pending.ToArray())
            if (pending.TryRemove(item.Key, out var value)) await value.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record PendingPairing(
        PairingCandidate Candidate,
        TcpClient Client,
        SslStream Stream,
        SemaphoreSlim WriteGate,
        X509Certificate2 ClientCertificate,
        X509Certificate2 ServerCertificate,
        string Password,
        DateTimeOffset CreatedAt) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Stream.Dispose(); Client.Dispose(); WriteGate.Dispose(); ClientCertificate.Dispose(); ServerCertificate.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.AndroidTv;

public sealed class AndroidTvRemoteProvider(IAndroidTvCredentialStore credentialStore) : IRemoteProvider, IAsyncDisposable
{
    public const string ProviderId = AndroidTvPairingProvider.ProviderId;
    public static IReadOnlyCollection<RemoteAction> Capabilities { get; } =
        [RemoteActions.PowerToggle, RemoteActions.VolumeUp, RemoteActions.VolumeDown, RemoteActions.MuteToggle,
         RemoteActions.Up, RemoteActions.Down, RemoteActions.Left, RemoteActions.Right, RemoteActions.Ok,
         RemoteActions.Back, RemoteActions.Home];

    private static readonly IReadOnlyDictionary<string, int> KeyCodes = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [RemoteActions.Home.Id] = 3,
        [RemoteActions.Back.Id] = 4,
        [RemoteActions.Up.Id] = 19,
        [RemoteActions.Down.Id] = 20,
        [RemoteActions.Left.Id] = 21,
        [RemoteActions.Right.Id] = 22,
        [RemoteActions.Ok.Id] = 23,
        [RemoteActions.VolumeUp.Id] = 24,
        [RemoteActions.VolumeDown.Id] = 25,
        [RemoteActions.PowerToggle.Id] = 26,
        [RemoteActions.MuteToggle.Id] = 164
    };

    private readonly ConcurrentDictionary<string, AndroidTvRemoteSession> sessions = new(StringComparer.OrdinalIgnoreCase);
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;

    public async Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        if (!string.Equals(route.ProviderId, Id, StringComparison.Ordinal)) return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        if (!route.Capabilities.Contains(action) || !KeyCodes.TryGetValue(action.Id, out var keyCode))
            return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);

        var credentials = await credentialStore.GetAsync(route.DeviceKey, cancellationToken).ConfigureAwait(false);
        if (credentials is null) return RemoteResult.Failed(RemoteErrorCode.PairingRequired);
        try
        {
            var session = sessions.GetOrAdd(route.DeviceKey, _ => new AndroidTvRemoteSession(credentials));
            await session.SendKeyAsync(keyCode, cancellationToken).ConfigureAwait(false);
            return RemoteResult.Accepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TimeoutException) { RemoveSession(route.DeviceKey); return RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown); }
        catch (AuthenticationException) { RemoveSession(route.DeviceKey); return RemoteResult.Failed(RemoteErrorCode.PairingRequired); }
        catch (IOException) { RemoveSession(route.DeviceKey); return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown); }
        catch (SocketException) { RemoveSession(route.DeviceKey); return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable); }
    }

    private void RemoveSession(string host)
    {
        if (sessions.TryRemove(host, out var session)) _ = session.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.Values) await session.DisposeAsync().ConfigureAwait(false);
        sessions.Clear();
    }

    private sealed class AndroidTvRemoteSession(AndroidTvCredentials credentials) : IAsyncDisposable
    {
        private readonly SemaphoreSlim connectGate = new(1, 1);
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private TcpClient? client;
        private SslStream? stream;
        private CancellationTokenSource? readerCts;
        private Task? readerTask;

        public async Task SendKeyAsync(int keyCode, CancellationToken ct)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            var active = stream ?? throw new IOException("Android TV session is disconnected.");
            await AndroidTvProtocol.WriteFrameAsync(active, AndroidTvProtocol.RemoteKeyInject(keyCode), writeGate, ct).ConfigureAwait(false);
        }

        private async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (stream is not null && client?.Connected == true) return;
            await connectGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (stream is not null && client?.Connected == true) return;
                await DisposeTransportAsync().ConfigureAwait(false);
                var pfx = Convert.FromBase64String(credentials.PfxBase64);
                using var certificate = X509CertificateLoader.LoadPkcs12(pfx, credentials.Password, X509KeyStorageFlags.Exportable);
                var tcp = new TcpClient();
                await tcp.ConnectAsync(credentials.Host, AndroidTvProtocol.RemotePort, ct).ConfigureAwait(false);
                var ssl = new SslStream(tcp.GetStream(), false, (_, remote, _, _) =>
                {
                    if (remote is null) return false;
                    var hash = Convert.ToHexString(SHA256.HashData(remote.GetRawCertData()));
                    return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(credentials.ServerCertificateSha256));
                });
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = credentials.Host,
                    ClientCertificates = new X509CertificateCollection { certificate },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, ct).ConfigureAwait(false);
                client = tcp; stream = ssl;
                await AndroidTvProtocol.WriteFrameAsync(ssl, AndroidTvProtocol.RemoteConfigure(), writeGate, ct).ConfigureAwait(false);
                readerCts = new CancellationTokenSource();
                readerTask = RunReaderAsync(ssl, readerCts.Token);
            }
            finally { connectGate.Release(); }
        }

        private async Task RunReaderAsync(SslStream active, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var message = await AndroidTvProtocol.ReadFrameAsync(active, ct).ConfigureAwait(false);
                    if (AndroidTvProtocol.TryReadPing(message, out var value))
                        await AndroidTvProtocol.WriteFrameAsync(active, AndroidTvProtocol.RemotePingResponse(value), writeGate, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (IOException) { }
        }

        private async ValueTask DisposeTransportAsync()
        {
            if (readerCts is not null) readerCts.Cancel();
            if (readerTask is not null)
            {
                try { await readerTask.ConfigureAwait(false); } catch { }
            }
            readerCts?.Dispose(); readerCts = null; readerTask = null;
            stream?.Dispose(); stream = null;
            client?.Dispose(); client = null;
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeTransportAsync().ConfigureAwait(false);
            connectGate.Dispose(); writeGate.Dispose();
        }
    }
}

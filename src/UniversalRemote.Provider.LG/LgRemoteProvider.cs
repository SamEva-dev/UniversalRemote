using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.LG;

public sealed class LgRemoteProvider(ILgCredentialStore store) : IRemoteProvider
{
    public const string ProviderId = LgPairingProvider.ProviderId;
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;
    public static IReadOnlyCollection<RemoteAction> Capabilities { get; } =
        [RemoteActions.VolumeUp, RemoteActions.VolumeDown, RemoteActions.MuteToggle,
         RemoteActions.Up, RemoteActions.Down, RemoteActions.Left, RemoteActions.Right, RemoteActions.Ok, RemoteActions.Back, RemoteActions.Home];

    public async Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        if (route.ProviderId != Id) return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        if (!route.Capabilities.Contains(action)) return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);
        var credentials = await store.GetAsync(route.DeviceKey, cancellationToken).ConfigureAwait(false);
        if (credentials is null) return RemoteResult.Failed(RemoteErrorCode.PairingRequired);
        try
        {
            using var socket = await ConnectAsync(credentials, cancellationToken).ConfigureAwait(false);
            if (action == RemoteActions.VolumeUp || action == RemoteActions.VolumeDown)
            {
                var uri = action == RemoteActions.VolumeUp ? "ssap://audio/volumeUp" : "ssap://audio/volumeDown";
                await SendRequestAsync(socket, uri, null, cancellationToken).ConfigureAwait(false);
            }
            else if (action == RemoteActions.MuteToggle) await ToggleMuteAsync(socket, cancellationToken).ConfigureAwait(false);
            else await SendPointerButtonAsync(socket, action, credentials, cancellationToken).ConfigureAwait(false);
            return RemoteResult.Accepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WebSocketException) { return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown); }
        catch (InvalidDataException) { return RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown); }
        catch (CryptographicException) { return RemoteResult.Failed(RemoteErrorCode.PairingRequired); }
    }

    private static async Task<ClientWebSocket> ConnectAsync(LgCredentials credentials, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, cert, _, _) =>
        {
            if (cert is null || string.IsNullOrWhiteSpace(credentials.ServerCertificateSha256)) return false;
            var actual = LgProtocol.Fingerprint(cert);
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(credentials.ServerCertificateSha256));
        };
        try
        {
        await socket.ConnectAsync(LgProtocol.UriFor(credentials.Host), ct).ConfigureAwait(false);
        await socket.SendAsync(Encoding.UTF8.GetBytes(LgProtocol.Register(credentials.ClientKey)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        while (LgProtocol.TryClientKey(await LgProtocol.ReceiveTextAsync(socket, ct).ConfigureAwait(false)) is null) { }
        return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    private static async Task<string> SendRequestAsync(ClientWebSocket socket, string uri, bool? payload, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        await socket.SendAsync(Encoding.UTF8.GetBytes(LgProtocol.Request(id, uri, payload)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        while (true)
        {
            var json = await LgProtocol.ReceiveTextAsync(socket, ct).ConfigureAwait(false);
            if (LgResponseValidation.MatchesSuccessfulResponse(json, id)) return json;
        }
    }

    private static async Task ToggleMuteAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var current = await SendRequestAsync(socket, "ssap://audio/getVolume", null, ct).ConfigureAwait(false);
        var muted = LgResponseValidation.ReadMute(current);
        await SendRequestAsync(socket, "ssap://audio/setMute", !muted, ct).ConfigureAwait(false);
    }

    private static async Task SendPointerButtonAsync(ClientWebSocket socket, RemoteAction action, LgCredentials credentials, CancellationToken ct)
    {
        var response = await SendRequestAsync(socket, "ssap://com.webos.service.networkinput/getPointerInputSocket", null, ct).ConfigureAwait(false);
        var path = LgProtocol.TrySocketPath(response) ?? throw new InvalidDataException("LG pointer socket path was not returned.");
        var pointerUri = LgResponseValidation.PointerUri(path, credentials.Host);
        using var pointer = new ClientWebSocket();
        pointer.Options.RemoteCertificateValidationCallback = (_, cert, _, _) =>
        {
            if (cert is null) return false;
            if (string.IsNullOrWhiteSpace(credentials.ServerCertificateSha256)) return false;
            var actual = LgProtocol.Fingerprint(cert);
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(credentials.ServerCertificateSha256));
        };
        await pointer.ConnectAsync(pointerUri, ct).ConfigureAwait(false);
        var key = action.Id switch
        {
            var x when x == RemoteActions.Up.Id => "UP", var x when x == RemoteActions.Down.Id => "DOWN",
            var x when x == RemoteActions.Left.Id => "LEFT", var x when x == RemoteActions.Right.Id => "RIGHT",
            var x when x == RemoteActions.Ok.Id => "ENTER", var x when x == RemoteActions.Back.Id => "BACK",
            var x when x == RemoteActions.Home.Id => "HOME", _ => throw new InvalidDataException("Unsupported LG pointer action.")
        };
        var text = Encoding.UTF8.GetBytes($"type:button\nname:{key}\n\n");
        await pointer.SendAsync(text, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }
}

using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.Samsung;

public sealed class SamsungRemoteProvider(ISamsungCredentialStore store) : IRemoteProvider
{
    public const string ProviderId = SamsungPairingProvider.ProviderId;
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;
    public static IReadOnlyCollection<RemoteAction> Capabilities { get; } =
        [RemoteActions.VolumeUp, RemoteActions.VolumeDown, RemoteActions.MuteToggle,
         RemoteActions.Up, RemoteActions.Down, RemoteActions.Left, RemoteActions.Right, RemoteActions.Ok,
         RemoteActions.Back, RemoteActions.Home];

    private static readonly IReadOnlyDictionary<string,string> Keys = new Dictionary<string,string>(StringComparer.Ordinal)
    {
        [RemoteActions.VolumeUp.Id]="KEY_VOLUP", [RemoteActions.VolumeDown.Id]="KEY_VOLDOWN",
        [RemoteActions.MuteToggle.Id]="KEY_MUTE", [RemoteActions.Up.Id]="KEY_UP", [RemoteActions.Down.Id]="KEY_DOWN",
        [RemoteActions.Left.Id]="KEY_LEFT", [RemoteActions.Right.Id]="KEY_RIGHT", [RemoteActions.Ok.Id]="KEY_ENTER",
        [RemoteActions.Back.Id]="KEY_RETURN", [RemoteActions.Home.Id]="KEY_HOME"
    };

    public async Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        if (route.ProviderId != Id) return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        if (!route.Capabilities.Contains(action) || !Keys.TryGetValue(action.Id, out var key)) return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);
        var credentials = await store.GetAsync(route.DeviceKey, cancellationToken).ConfigureAwait(false);
        if (credentials is null) return RemoteResult.Failed(RemoteErrorCode.PairingRequired);
        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null || string.IsNullOrWhiteSpace(credentials.ServerCertificateSha256)) return false;
                var actual = SamsungProtocol.Fingerprint(certificate);
                return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(credentials.ServerCertificateSha256));
            };
            await socket.ConnectAsync(SamsungProtocol.BuildUri(route.DeviceKey, credentials.Token), cancellationToken).ConfigureAwait(false);
            var message = Encoding.UTF8.GetBytes(SamsungProtocol.KeyMessage(key));
            await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            return RemoteResult.Accepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WebSocketException) { return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown); }
        catch (CryptographicException) { return RemoteResult.Failed(RemoteErrorCode.PairingRequired); }
    }
}

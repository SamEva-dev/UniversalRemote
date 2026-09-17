using UniversalRemote.Remote.Abstractions;
namespace UniversalRemote.Remote.Provider.Freebox;

public sealed class FreeboxRemoteProvider(HttpClient httpClient, IFreeboxRemoteCodeStore codeStore) : IRemoteProvider
{
    public const string ProviderId = "freebox-revolution";
    public static IReadOnlyCollection<RemoteAction> Capabilities { get; } =
        [RemoteActions.PowerToggle, RemoteActions.VolumeUp, RemoteActions.VolumeDown, RemoteActions.MuteToggle,
         RemoteActions.Up, RemoteActions.Down, RemoteActions.Left, RemoteActions.Right, RemoteActions.Ok,
         RemoteActions.Back, RemoteActions.Home];
    private static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RemoteActions.PowerToggle.Id] = "power",
        [RemoteActions.VolumeUp.Id] = "vol_inc",
        [RemoteActions.VolumeDown.Id] = "vol_dec",
        [RemoteActions.MuteToggle.Id] = "mute",
        [RemoteActions.Up.Id] = "up",
        [RemoteActions.Down.Id] = "down",
        [RemoteActions.Left.Id] = "left",
        [RemoteActions.Right.Id] = "right",
        [RemoteActions.Ok.Id] = "ok",
        [RemoteActions.Back.Id] = "back",
        [RemoteActions.Home.Id] = "home"
    };
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;

    public async Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        if (!string.Equals(route.ProviderId, Id, StringComparison.Ordinal)) return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        if (!route.Capabilities.Contains(action) || !Keys.TryGetValue(action.Id, out var key)) return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);
        var code = await codeStore.GetAsync(route.DeviceKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(code)) return RemoteResult.Failed(RemoteErrorCode.PairingRequired);
        var host = string.IsNullOrWhiteSpace(route.DeviceKey) ? "hd1.freebox.fr" : route.DeviceKey;
        var uri = new UriBuilder(Uri.UriSchemeHttp, host) { Path = "/pub/remote_control", Query = $"code={Uri.EscapeDataString(code)}&key={Uri.EscapeDataString(key)}" }.Uri;
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? RemoteResult.Accepted() : RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { return RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown); }
        catch (HttpRequestException) { return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown); }
    }
}

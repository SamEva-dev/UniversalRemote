using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Provider.OrangeTv;

public sealed class OrangeTvRemoteProvider(HttpClient httpClient) : IRemoteProvider
{
    public const string ProviderId = "orange-tv-uhd";

    public static IReadOnlyCollection<RemoteAction> Capabilities { get; } =
    [
        RemoteActions.PowerToggle,
        RemoteActions.VolumeUp,
        RemoteActions.VolumeDown,
        RemoteActions.MuteToggle,
        RemoteActions.Up,
        RemoteActions.Down,
        RemoteActions.Left,
        RemoteActions.Right,
        RemoteActions.Ok,
        RemoteActions.Back,
        RemoteActions.Menu,
        RemoteActions.ChannelUp,
        RemoteActions.ChannelDown,
        RemoteActions.PlayPause,
        RemoteActions.Rewind,
        RemoteActions.FastForward,
        RemoteActions.Record
    ];

    private static readonly IReadOnlyDictionary<string, int> Keys = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [RemoteActions.PowerToggle.Id] = 116,
        [RemoteActions.VolumeUp.Id] = 115,
        [RemoteActions.VolumeDown.Id] = 114,
        [RemoteActions.MuteToggle.Id] = 113,
        [RemoteActions.Up.Id] = 103,
        [RemoteActions.Down.Id] = 108,
        [RemoteActions.Left.Id] = 105,
        [RemoteActions.Right.Id] = 106,
        [RemoteActions.Ok.Id] = 352,
        [RemoteActions.Back.Id] = 158,
        [RemoteActions.Menu.Id] = 139,
        [RemoteActions.ChannelUp.Id] = 402,
        [RemoteActions.ChannelDown.Id] = 403,
        [RemoteActions.PlayPause.Id] = 164,
        [RemoteActions.Rewind.Id] = 168,
        [RemoteActions.FastForward.Id] = 159,
        [RemoteActions.Record.Id] = 167
    };

    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;

    public async Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        if (!string.Equals(route.ProviderId, Id, StringComparison.Ordinal))
            return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        if (!route.Capabilities.Contains(action) || !Keys.TryGetValue(action.Id, out var keyCode))
            return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);
        if (!OrangeTvEndpoint.TryNormalizeLocalAddress(route.DeviceKey, out var address))
            return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, OrangeTvEndpoint.Command(address, keyCode));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? RemoteResult.Accepted()
                : RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { return RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown); }
        catch (HttpRequestException) { return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown); }
    }

    public static DeviceRoute CreateRoute(string localAddress)
    {
        if (!OrangeTvEndpoint.TryNormalizeLocalAddress(localAddress, out var normalized))
            throw new ArgumentException("Orange TV route requires a private local IP address.", nameof(localAddress));
        return new DeviceRoute(ProviderId, normalized, Capabilities);
    }
}

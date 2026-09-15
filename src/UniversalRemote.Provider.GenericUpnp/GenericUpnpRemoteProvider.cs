using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.GenericUpnp;

public sealed class GenericUpnpRemoteProvider(HttpClient httpClient) : IRemoteProvider
{
    public const string ProviderId = "generic-upnp";
    private const string RenderingControl = "urn:schemas-upnp-org:service:RenderingControl:";
    private readonly ConcurrentDictionary<string, UpnpEndpoint> endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly UpnpSoapClient soap = new(httpClient);
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Preview;
    public static IReadOnlyCollection<RemoteAction> Capabilities { get; } = [RemoteActions.VolumeUp, RemoteActions.VolumeDown, RemoteActions.MuteToggle];

    public static DeviceRoute CreateRoute(string descriptionLocation) => new(ProviderId, descriptionLocation, Capabilities);

    public async Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        if (!string.Equals(route.ProviderId, Id, StringComparison.Ordinal)) return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        if (!route.Capabilities.Contains(action) || !Capabilities.Contains(action)) return RemoteResult.Failed(RemoteErrorCode.UnsupportedAction);
        if (!Uri.TryCreate(route.DeviceKey, UriKind.Absolute, out var location)) return RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable);
        try
        {
            var endpoint = await GetEndpointAsync(location, cancellationToken).ConfigureAwait(false);
            if (action == RemoteActions.MuteToggle) await ToggleMuteAsync(endpoint, cancellationToken).ConfigureAwait(false);
            else await ChangeVolumeAsync(endpoint, action == RemoteActions.VolumeUp ? 2 : -2, cancellationToken).ConfigureAwait(false);
            return RemoteResult.Accepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { return RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown); }
        catch (HttpRequestException) { return RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown); }
        catch (InvalidDataException) { return RemoteResult.Failed(RemoteErrorCode.ProviderFailure); }
    }

    private async Task ChangeVolumeAsync(UpnpEndpoint ep, int delta, CancellationToken ct)
    {
        var current = await soap.InvokeAsync(ep.ControlUri, ep.ServiceType, "GetVolume", CommonArgs(), ct).ConfigureAwait(false);
        if (!int.TryParse(UpnpSoapClient.Value(current, "CurrentVolume"), CultureInfo.InvariantCulture, out var value)) throw new InvalidDataException("Invalid UPnP volume.");
        var desired = Math.Clamp(value + delta, 0, 100);
        await soap.InvokeAsync(ep.ControlUri, ep.ServiceType, "SetVolume", new Dictionary<string, string>(CommonArgs()) { ["DesiredVolume"] = desired.ToString(CultureInfo.InvariantCulture) }, ct).ConfigureAwait(false);
    }

    private async Task ToggleMuteAsync(UpnpEndpoint ep, CancellationToken ct)
    {
        var current = await soap.InvokeAsync(ep.ControlUri, ep.ServiceType, "GetMute", CommonArgs(), ct).ConfigureAwait(false);
        var muted = UpnpSoapClient.Value(current, "CurrentMute") is "1" or "true";
        await soap.InvokeAsync(ep.ControlUri, ep.ServiceType, "SetMute", new Dictionary<string, string>(CommonArgs()) { ["DesiredMute"] = muted ? "0" : "1" }, ct).ConfigureAwait(false);
    }

    private static Dictionary<string, string> CommonArgs() => new() { ["InstanceID"] = "0", ["Channel"] = "Master" };

    private async Task<UpnpEndpoint> GetEndpointAsync(Uri location, CancellationToken ct)
    {
        if (endpoints.TryGetValue(location.AbsoluteUri, out var cached)) return cached;
        using var response = await httpClient.GetAsync(location, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false);
        var service = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "service" &&
            x.Elements().Any(y => y.Name.LocalName == "serviceType" && y.Value.StartsWith(RenderingControl, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidDataException("RenderingControl service not found.");
        var serviceType = service.Elements().First(x => x.Name.LocalName == "serviceType").Value;
        var controlUrl = service.Elements().First(x => x.Name.LocalName == "controlURL").Value;
        var controlUri = new Uri(location, controlUrl);
        var endpoint = new UpnpEndpoint(serviceType, controlUri);
        endpoints[location.AbsoluteUri] = endpoint;
        return endpoint;
    }

    private sealed record UpnpEndpoint(string ServiceType, Uri ControlUri);
}

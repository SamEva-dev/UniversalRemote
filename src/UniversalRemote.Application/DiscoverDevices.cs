using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Discovery;

namespace UniversalRemote.Application;

public sealed record DiscoverDevices(int TimeoutMilliseconds = 3000) : IRequest<IReadOnlyList<DiscoveredDeviceSummary>>;

public sealed record DiscoveredDeviceSummary(
    string DiscoveryId,
    string DisplayName,
    string? HostName,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Sources,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class DiscoverDevicesValidator : AbstractValidator<DiscoverDevices>
{
    public DiscoverDevicesValidator()
        => RuleFor(x => x.TimeoutMilliseconds).InclusiveBetween(250, 30_000);
}

public sealed class DiscoverDevicesHandler(IDeviceDiscovery discovery)
    : IRequestHandler<DiscoverDevices, IReadOnlyList<DiscoveredDeviceSummary>>
{
    public async Task<IReadOnlyList<DiscoveredDeviceSummary>> Handle(DiscoverDevices request, CancellationToken ct)
    {
        var items = await discovery.DiscoverAsync(
            new DiscoveryScanOptions { Timeout = TimeSpan.FromMilliseconds(request.TimeoutMilliseconds) }, ct).ConfigureAwait(false);
        return items.Select(x => new DiscoveredDeviceSummary(
            x.DiscoveryId,
            x.DisplayName,
            x.HostName,
            x.Addresses.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            x.Services.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            x.Sources.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            x.Metadata)).ToArray();
    }
}

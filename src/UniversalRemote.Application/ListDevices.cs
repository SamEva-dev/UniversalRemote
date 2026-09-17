using DomainRelay.Abstractions;
using DomainRelay.Mapping.Abstractions.Configuration;
using DomainRelay.Mapping.Abstractions.Profiles;
using DomainRelay.Mapping.Abstractions.Services;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Application;

public sealed record ListDevices : IRequest<IReadOnlyList<DeviceSummary>>;

public sealed class DeviceSummary
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class DeviceMappingProfile : MappingProfile
{
    public override void Configure(IMappingConfiguration configuration)
        => configuration.CreateMap<Device, DeviceSummary>();
}

public sealed class ListDevicesHandler(IDeviceRepository devices, IObjectMapper mapper)
    : IRequestHandler<ListDevices, IReadOnlyList<DeviceSummary>>
{
    public async Task<IReadOnlyList<DeviceSummary>> Handle(ListDevices request, CancellationToken ct)
    {
        var items = await devices.ListAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return items.Select(device => mapper.Map<Device, DeviceSummary>(device)).ToArray();
    }
}

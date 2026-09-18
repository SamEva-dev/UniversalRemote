using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Application;

/// <summary>Changes only user-facing metadata. Provider routes and credentials are preserved untouched.</summary>
public sealed record RenameDevice(Guid DeviceId, string DisplayName) : IRequest<DeviceSummary>;

public sealed class RenameDeviceValidator : AbstractValidator<RenameDevice>
{
    public RenameDeviceValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
    }
}

public sealed class RenameDeviceHandler(
    IDeviceRepository devices,
    IDeviceRegistrar registrar)
    : IRequestHandler<RenameDevice, DeviceSummary>
{
    public async Task<DeviceSummary> Handle(RenameDevice request, CancellationToken ct)
    {
        var current = await devices.FindAsync(request.DeviceId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Device not found.");

        var renamed = new Device(current.Id, request.DisplayName.Trim(), current.Routes);
        await registrar.UpsertAsync(renamed, ct).ConfigureAwait(false);

        return new DeviceSummary
        {
            Id = renamed.Id,
            DisplayName = renamed.DisplayName
        };
    }
}

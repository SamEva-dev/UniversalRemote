using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Presentation;

namespace UniversalRemote.Remote.Application;

public sealed record GetRemoteUiModel(Guid DeviceId) : IRequest<RemoteUiModel?>;

public sealed class GetRemoteUiModelValidator : AbstractValidator<GetRemoteUiModel>
{
    public GetRemoteUiModelValidator()
        => RuleFor(x => x.DeviceId).NotEmpty().WithMessage("A device identifier is required.");
}

public sealed class GetRemoteUiModelHandler(IDeviceRepository devices, IFavoriteRepository favorites, IRemoteUiModelBuilder builder)
    : IRequestHandler<GetRemoteUiModel, RemoteUiModel?>
{
    public async Task<RemoteUiModel?> Handle(GetRemoteUiModel request, CancellationToken ct)
    {
        var device = await devices.FindAsync(request.DeviceId, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (device is null) return null;

        var shortcuts = await favorites.ListAsync(device.Id, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return RemoteUiFavoriteProjection.Apply(builder.Build(device), shortcuts.Select(x => x.Action.Id).ToArray());
    }
}

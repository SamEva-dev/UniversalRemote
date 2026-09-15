using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Abstractions;
using UniversalRemote.Presentation;

namespace UniversalRemote.Application;

public sealed record GetRemoteUiModel(Guid DeviceId) : IRequest<RemoteUiModel?>;

public sealed class GetRemoteUiModelValidator : AbstractValidator<GetRemoteUiModel>
{
    public GetRemoteUiModelValidator()
        => RuleFor(x => x.DeviceId).NotEmpty().WithMessage("A device identifier is required.");
}

public sealed class GetRemoteUiModelHandler(IDeviceRepository devices, IRemoteUiModelBuilder builder)
    : IRequestHandler<GetRemoteUiModel, RemoteUiModel?>
{
    public async Task<RemoteUiModel?> Handle(GetRemoteUiModel request, CancellationToken ct)
    {
        var device = await devices.FindAsync(request.DeviceId, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return device is null ? null : builder.Build(device);
    }
}

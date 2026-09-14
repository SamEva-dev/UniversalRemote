using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Application;

public sealed record ExecuteRemoteAction(Guid DeviceId, RemoteAction Action) : IRequest<RemoteResult>;

public sealed class ExecuteRemoteActionValidator : AbstractValidator<ExecuteRemoteAction>
{
    public ExecuteRemoteActionValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().WithMessage("A device identifier is required.");
        RuleFor(x => x.Action).NotNull().WithMessage("An action is required.");
    }
}

public sealed class ExecuteRemoteActionHandler(IRemoteControl remote) : IRequestHandler<ExecuteRemoteAction, RemoteResult>
{
    public Task<RemoteResult> Handle(ExecuteRemoteAction request, CancellationToken ct)
        => remote.ExecuteAsync(request.DeviceId, request.Action, ct);
}

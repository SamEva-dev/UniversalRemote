using DomainRelay.Abstractions;
using FluentValidation;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Application;

public enum ActivityStepInputKind { RemoteAction, Delay }

public sealed record ActivityStepInput(
    int Position,
    ActivityStepInputKind Kind,
    Guid? DeviceId = null,
    string? ActionId = null,
    int? DelayMilliseconds = null);

public sealed record ActivityStepSummary(
    int Position,
    ActivityStepInputKind Kind,
    Guid? DeviceId,
    string? ActionId,
    int? DelayMilliseconds);

public sealed record ActivitySummary(Guid Id, string Name, Guid? RoomId, IReadOnlyList<ActivityStepSummary> Steps);

public sealed record ListActivities : IRequest<IReadOnlyList<ActivitySummary>>;
public sealed record GetActivity(Guid ActivityId) : IRequest<ActivitySummary?>;
public sealed record CreateActivity(string Name, Guid? RoomId = null) : IRequest<ActivitySummary>;
public sealed record SaveActivity(Guid ActivityId, string Name, Guid? RoomId, IReadOnlyList<ActivityStepInput> Steps) : IRequest<ActivitySummary>;
public sealed record DeleteActivity(Guid ActivityId) : IRequest<bool>;
public sealed record RunActivity(
    Guid ActivityId,
    ActivityFailurePolicy FailurePolicy = ActivityFailurePolicy.StopOnFirstNonAccepted) : IRequest<ActivityRunReport>;

public sealed class GetActivityValidator : AbstractValidator<GetActivity>
{
    public GetActivityValidator() => RuleFor(x => x.ActivityId).NotEmpty();
}

public sealed class CreateActivityValidator : AbstractValidator<CreateActivity>
{
    public CreateActivityValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.RoomId).Must(x => x is null || x.Value != Guid.Empty).WithMessage("RoomId must not be empty when specified.");
    }
}

public sealed class SaveActivityValidator : AbstractValidator<SaveActivity>
{
    public SaveActivityValidator()
    {
        RuleFor(x => x.ActivityId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.RoomId).Must(x => x is null || x.Value != Guid.Empty).WithMessage("RoomId must not be empty when specified.");
        RuleFor(x => x.Steps).NotNull().Must(x => x is not null && x.Count <= Activity.MaximumSteps)
            .WithMessage($"An activity may contain at most {Activity.MaximumSteps} steps.");
        RuleForEach(x => x.Steps).SetValidator(new ActivityStepInputValidator());
        RuleFor(x => x.Steps).Must(HaveContiguousPositions)
            .WithMessage("Activity step positions must be contiguous, ordered and start at zero.");
    }

    private static bool HaveContiguousPositions(IReadOnlyList<ActivityStepInput>? steps)
        => steps is not null && steps.Select(x => x.Position).SequenceEqual(Enumerable.Range(0, steps.Count));
}

public sealed class DeleteActivityValidator : AbstractValidator<DeleteActivity>
{
    public DeleteActivityValidator() => RuleFor(x => x.ActivityId).NotEmpty();
}

public sealed class RunActivityValidator : AbstractValidator<RunActivity>
{
    public RunActivityValidator()
    {
        RuleFor(x => x.ActivityId).NotEmpty();
        RuleFor(x => x.FailurePolicy).IsInEnum();
    }
}

public sealed class ActivityStepInputValidator : AbstractValidator<ActivityStepInput>
{
    public ActivityStepInputValidator()
    {
        RuleFor(x => x.Position).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Custom((step, context) =>
        {
            switch (step.Kind)
            {
                case ActivityStepInputKind.RemoteAction:
                    if (step.DeviceId is null || step.DeviceId == Guid.Empty)
                        context.AddFailure(nameof(step.DeviceId), "A remote-action step requires a DeviceId.");
                    if (string.IsNullOrWhiteSpace(step.ActionId))
                        context.AddFailure(nameof(step.ActionId), "A remote-action step requires an ActionId.");
                    else
                    {
                        try { _ = new RemoteAction(step.ActionId); }
                        catch (ArgumentException) { context.AddFailure(nameof(step.ActionId), "The ActionId is invalid."); }
                    }
                    if (step.DelayMilliseconds is not null)
                        context.AddFailure(nameof(step.DelayMilliseconds), "A remote-action step cannot also define a delay.");
                    break;

                case ActivityStepInputKind.Delay:
                    if (step.DeviceId is not null) context.AddFailure(nameof(step.DeviceId), "A delay step cannot target a device.");
                    if (step.ActionId is not null) context.AddFailure(nameof(step.ActionId), "A delay step cannot define an action.");
                    if (step.DelayMilliseconds is null
                        || step.DelayMilliseconds.Value < DelayActivityStep.MinimumDuration.TotalMilliseconds
                        || step.DelayMilliseconds.Value > DelayActivityStep.MaximumDuration.TotalMilliseconds)
                        context.AddFailure(nameof(step.DelayMilliseconds), "Delay must be between 50 and 60000 milliseconds.");
                    break;

                default:
                    context.AddFailure(nameof(step.Kind), "Unsupported activity step kind.");
                    break;
            }
        });
    }
}

public sealed class ListActivitiesHandler(IActivityRepository activities)
    : IRequestHandler<ListActivities, IReadOnlyList<ActivitySummary>>
{
    public async Task<IReadOnlyList<ActivitySummary>> Handle(ListActivities request, CancellationToken ct)
        => (await activities.ListAsync(ct).ConfigureAwait(false)).Select(ActivityProjection.ToSummary).ToArray();
}

public sealed class GetActivityHandler(IActivityRepository activities)
    : IRequestHandler<GetActivity, ActivitySummary?>
{
    public async Task<ActivitySummary?> Handle(GetActivity request, CancellationToken ct)
    {
        var activity = await activities.FindAsync(request.ActivityId, ct).ConfigureAwait(false);
        return activity is null ? null : ActivityProjection.ToSummary(activity);
    }
}

public sealed class CreateActivityHandler(IActivityRepository activities, IRoomRepository rooms)
    : IRequestHandler<CreateActivity, ActivitySummary>
{
    public async Task<ActivitySummary> Handle(CreateActivity request, CancellationToken ct)
    {
        await EnsureRoomExistsAsync(rooms, request.RoomId, ct).ConfigureAwait(false);
        var activity = new Activity(Guid.NewGuid(), request.Name, request.RoomId);
        return ActivityProjection.ToSummary(await activities.SaveAsync(activity, ct).ConfigureAwait(false));
    }

    internal static async Task EnsureRoomExistsAsync(IRoomRepository rooms, Guid? roomId, CancellationToken ct)
    {
        if (roomId is not null && await rooms.FindAsync(roomId.Value, ct).ConfigureAwait(false) is null)
            throw new KeyNotFoundException("Room not found.");
    }
}

public sealed class SaveActivityHandler(IActivityRepository activities, IDeviceRepository devices, IRoomRepository rooms)
    : IRequestHandler<SaveActivity, ActivitySummary>
{
    public async Task<ActivitySummary> Handle(SaveActivity request, CancellationToken ct)
    {
        if (await activities.FindAsync(request.ActivityId, ct).ConfigureAwait(false) is null)
            throw new KeyNotFoundException("Activity not found.");

        await CreateActivityHandler.EnsureRoomExistsAsync(rooms, request.RoomId, ct).ConfigureAwait(false);
        var steps = new ActivityStep[request.Steps.Count];
        for (var i = 0; i < request.Steps.Count; i++)
            steps[i] = await ToDomainStepAsync(request.Steps[i], devices, ct).ConfigureAwait(false);

        var activity = new Activity(request.ActivityId, request.Name, request.RoomId, steps);
        return ActivityProjection.ToSummary(await activities.SaveAsync(activity, ct).ConfigureAwait(false));
    }

    private static async Task<ActivityStep> ToDomainStepAsync(ActivityStepInput input, IDeviceRepository devices, CancellationToken ct)
    {
        if (input.Kind == ActivityStepInputKind.Delay)
            return new DelayActivityStep(input.Position, TimeSpan.FromMilliseconds(input.DelayMilliseconds!.Value));

        var deviceId = input.DeviceId!.Value;
        var device = await devices.FindAsync(deviceId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Device not found.");
        var action = new RemoteAction(input.ActionId!);
        if (!device.Capabilities.Contains(action))
            throw new InvalidOperationException("The selected action is not supported by the device.");
        return new RemoteActionActivityStep(input.Position, deviceId, action);
    }
}

public sealed class DeleteActivityHandler(IActivityRepository activities)
    : IRequestHandler<DeleteActivity, bool>
{
    public Task<bool> Handle(DeleteActivity request, CancellationToken ct) => activities.DeleteAsync(request.ActivityId, ct);
}

public sealed class RunActivityHandler(IActivityRepository activities, IActivityRunner runner)
    : IRequestHandler<RunActivity, ActivityRunReport>
{
    public async Task<ActivityRunReport> Handle(RunActivity request, CancellationToken ct)
    {
        var activity = await activities.FindAsync(request.ActivityId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Activity not found.");
        return await runner.RunAsync(activity, request.FailurePolicy, ct).ConfigureAwait(false);
    }
}

internal static class ActivityProjection
{
    public static ActivitySummary ToSummary(Activity activity)
        => new(activity.Id, activity.Name, activity.RoomId, activity.Steps.Select(ToSummary).ToArray());

    private static ActivityStepSummary ToSummary(ActivityStep step) => step switch
    {
        RemoteActionActivityStep action => new(action.Position, ActivityStepInputKind.RemoteAction, action.DeviceId, action.Action.Id, null),
        DelayActivityStep delay => new(delay.Position, ActivityStepInputKind.Delay, null, null, checked((int)delay.Duration.TotalMilliseconds)),
        _ => throw new NotSupportedException("Unsupported ActivityStep type.")
    };
}

using UniversalRemote.Abstractions;

namespace UniversalRemote.Core;

/// <summary>Delay seam used by the Activity runner and deterministic tests.</summary>
public interface IActivityDelayScheduler
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

internal sealed class SystemActivityDelayScheduler : IActivityDelayScheduler
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        => Task.Delay(duration, cancellationToken);
}

/// <summary>
/// Executes persisted Activity steps in strict order. Every command is attempted at most once.
/// Unknown delivery is never retried because the physical device may already have acted.
/// </summary>
public sealed class ActivityRunner(IRemoteControl remote, IActivityDelayScheduler delays) : IActivityRunner
{
    public async Task<ActivityRunReport> RunAsync(
        Activity activity,
        ActivityFailurePolicy failurePolicy = ActivityFailurePolicy.StopOnFirstNonAccepted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!Enum.IsDefined(failurePolicy))
            throw new ArgumentOutOfRangeException(nameof(failurePolicy));

        var reports = new ActivityStepRunReport[activity.Steps.Count];
        var hasIssues = false;

        for (var i = 0; i < activity.Steps.Count; i++)
        {
            var step = activity.Steps[i];
            if (cancellationToken.IsCancellationRequested)
            {
                FillNotRun(reports, activity.Steps, i);
                return Report(activity, failurePolicy, ActivityRunStatus.Cancelled, reports);
            }

            switch (step)
            {
                case DelayActivityStep delay:
                    try
                    {
                        await delays.DelayAsync(delay.Duration, cancellationToken).ConfigureAwait(false);
                        reports[i] = new ActivityStepRunReport(
                            delay.Position,
                            ActivityStepRunStatus.DelayCompleted,
                            Delay: delay.Duration);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        reports[i] = new ActivityStepRunReport(
                            delay.Position,
                            ActivityStepRunStatus.Cancelled,
                            Delay: delay.Duration);
                        FillNotRun(reports, activity.Steps, i + 1);
                        return Report(activity, failurePolicy, ActivityRunStatus.Cancelled, reports);
                    }
                    break;

                case RemoteActionActivityStep action:
                {
                    RemoteResult result;
                    try
                    {
                        result = await remote.ExecuteAsync(action.DeviceId, action.Action, cancellationToken).ConfigureAwait(false)
                            ?? RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // The transport may already have emitted the command. Record uncertainty and never replay it.
                        reports[i] = new ActivityStepRunReport(
                            action.Position,
                            ActivityStepRunStatus.Cancelled,
                            action.DeviceId,
                            action.Action,
                            Error: null,
                            Delivery: DeliveryState.Unknown);
                        FillNotRun(reports, activity.Steps, i + 1);
                        return Report(activity, failurePolicy, ActivityRunStatus.Cancelled, reports);
                    }
                    catch (Exception)
                    {
                        // IRemoteControl is expected to sanitize transport exceptions, but keep the runner boundary safe too.
                        result = RemoteResult.Failed(RemoteErrorCode.ProviderFailure, DeliveryState.Unknown);
                    }

                    var status = ToStepStatus(result);
                    reports[i] = new ActivityStepRunReport(
                        action.Position,
                        status,
                        action.DeviceId,
                        action.Action,
                        Error: result.Error == RemoteErrorCode.None ? null : result.Error,
                        Delivery: result.Delivery);

                    if (status is ActivityStepRunStatus.Failed or ActivityStepRunStatus.Unknown)
                    {
                        hasIssues = true;
                        if (failurePolicy == ActivityFailurePolicy.StopOnFirstNonAccepted)
                        {
                            FillNotRun(reports, activity.Steps, i + 1);
                            return Report(activity, failurePolicy, ActivityRunStatus.StoppedOnNonAccepted, reports);
                        }
                    }
                    break;
                }

                default:
                    throw new NotSupportedException($"Unsupported ActivityStep type: {step.GetType().Name}.");
            }
        }

        return Report(
            activity,
            failurePolicy,
            hasIssues ? ActivityRunStatus.CompletedWithIssues : ActivityRunStatus.Completed,
            reports);
    }

    private static ActivityStepRunStatus ToStepStatus(RemoteResult result)
    {
        if (result.IsSuccess && result.Delivery == DeliveryState.Accepted)
            return ActivityStepRunStatus.Accepted;
        if (result.Delivery == DeliveryState.Unknown)
            return ActivityStepRunStatus.Unknown;
        return ActivityStepRunStatus.Failed;
    }

    private static void FillNotRun(ActivityStepRunReport[] reports, IReadOnlyList<ActivityStep> steps, int startIndex)
    {
        for (var i = startIndex; i < steps.Count; i++)
        {
            reports[i] = steps[i] switch
            {
                RemoteActionActivityStep action => new ActivityStepRunReport(
                    action.Position,
                    ActivityStepRunStatus.NotRun,
                    action.DeviceId,
                    action.Action),
                DelayActivityStep delay => new ActivityStepRunReport(
                    delay.Position,
                    ActivityStepRunStatus.NotRun,
                    Delay: delay.Duration),
                _ => throw new NotSupportedException($"Unsupported ActivityStep type: {steps[i].GetType().Name}.")
            };
        }
    }

    private static ActivityRunReport Report(
        Activity activity,
        ActivityFailurePolicy failurePolicy,
        ActivityRunStatus status,
        ActivityStepRunReport[] reports)
        => new(activity.Id, activity.Name, failurePolicy, status, Array.AsReadOnly(reports));
}

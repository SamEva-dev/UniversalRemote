using FluentValidation;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Core;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class ActivityRunnerTests
{
    [Fact]
    public async Task Runner_executes_actions_and_delays_in_strict_order()
    {
        var trace = new List<string>();
        var remote = new DelegateRemoteControl((_, action, _) =>
        {
            trace.Add($"action:{action.Id}");
            return Task.FromResult(RemoteResult.Accepted());
        });
        var delays = new RecordingDelayScheduler(trace);
        var runner = new ActivityRunner(remote, delays);
        var deviceId = Guid.NewGuid();
        var activity = new Activity(Guid.NewGuid(), "Film", steps:
        [
            new RemoteActionActivityStep(0, deviceId, RemoteActions.PowerToggle),
            new DelayActivityStep(1, TimeSpan.FromMilliseconds(750)),
            new RemoteActionActivityStep(2, deviceId, RemoteActions.VolumeUp)
        ]);

        var report = await runner.RunAsync(activity);

        Assert.Equal(ActivityRunStatus.Completed, report.Status);
        Assert.Equal(
            ["action:power.toggle", "delay:750", "action:volume.up"],
            trace);
        Assert.Equal(
            [ActivityStepRunStatus.Accepted, ActivityStepRunStatus.DelayCompleted, ActivityStepRunStatus.Accepted],
            report.Steps.Select(x => x.Status).ToArray());
    }

    [Fact]
    public async Task Default_policy_stops_after_unknown_result_without_retrying_or_running_later_steps()
    {
        var calls = 0;
        var remote = new DelegateRemoteControl((_, _, _) =>
        {
            calls++;
            return Task.FromResult(RemoteResult.Failed(RemoteErrorCode.Timeout, DeliveryState.Unknown));
        });
        var runner = new ActivityRunner(remote, new RecordingDelayScheduler([]));
        var deviceId = Guid.NewGuid();
        var activity = TwoActionActivity(deviceId);

        var report = await runner.RunAsync(activity);

        Assert.Equal(1, calls);
        Assert.Equal(ActivityRunStatus.StoppedOnNonAccepted, report.Status);
        Assert.Equal(ActivityStepRunStatus.Unknown, report.Steps[0].Status);
        Assert.Equal(DeliveryState.Unknown, report.Steps[0].Delivery);
        Assert.Equal(ActivityStepRunStatus.NotRun, report.Steps[1].Status);
    }

    [Fact]
    public async Task Continue_policy_moves_forward_after_unknown_but_still_never_retries_the_uncertain_step()
    {
        var calls = new List<string>();
        var invocation = 0;
        var remote = new DelegateRemoteControl((_, action, _) =>
        {
            calls.Add(action.Id);
            invocation++;
            return Task.FromResult(invocation == 1
                ? RemoteResult.Failed(RemoteErrorCode.TransportFailure, DeliveryState.Unknown)
                : RemoteResult.Accepted());
        });
        var runner = new ActivityRunner(remote, new RecordingDelayScheduler([]));
        var activity = TwoActionActivity(Guid.NewGuid());

        var report = await runner.RunAsync(activity, ActivityFailurePolicy.ContinueAfterNonAccepted);

        Assert.Equal(["power.toggle", "volume.up"], calls);
        Assert.Equal(ActivityRunStatus.CompletedWithIssues, report.Status);
        Assert.Equal(ActivityStepRunStatus.Unknown, report.Steps[0].Status);
        Assert.Equal(ActivityStepRunStatus.Accepted, report.Steps[1].Status);
    }

    [Fact]
    public async Task Failed_not_sent_result_stops_default_policy_and_is_reported_as_failed()
    {
        var calls = 0;
        var remote = new DelegateRemoteControl((_, _, _) =>
        {
            calls++;
            return Task.FromResult(RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable));
        });
        var runner = new ActivityRunner(remote, new RecordingDelayScheduler([]));

        var report = await runner.RunAsync(TwoActionActivity(Guid.NewGuid()));

        Assert.Equal(1, calls);
        Assert.Equal(ActivityRunStatus.StoppedOnNonAccepted, report.Status);
        Assert.Equal(ActivityStepRunStatus.Failed, report.Steps[0].Status);
        Assert.Equal(RemoteErrorCode.ProviderUnavailable, report.Steps[0].Error);
        Assert.Equal(DeliveryState.NotSent, report.Steps[0].Delivery);
        Assert.Equal(ActivityStepRunStatus.NotRun, report.Steps[1].Status);
    }

    [Fact]
    public async Task Cancellation_during_delay_returns_partial_report_and_does_not_run_following_command()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var remote = new DelegateRemoteControl((_, _, _) =>
        {
            calls++;
            return Task.FromResult(RemoteResult.Accepted());
        });
        var delays = new DelegateDelayScheduler((_, token) =>
        {
            cts.Cancel();
            return Task.FromCanceled(token);
        });
        var runner = new ActivityRunner(remote, delays);
        var activity = new Activity(Guid.NewGuid(), "Cancel delay", steps:
        [
            new DelayActivityStep(0, TimeSpan.FromMilliseconds(100)),
            new RemoteActionActivityStep(1, Guid.NewGuid(), RemoteActions.PowerToggle)
        ]);

        var report = await runner.RunAsync(activity, cancellationToken: cts.Token);

        Assert.Equal(ActivityRunStatus.Cancelled, report.Status);
        Assert.Equal(ActivityStepRunStatus.Cancelled, report.Steps[0].Status);
        Assert.Equal(ActivityStepRunStatus.NotRun, report.Steps[1].Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Cancellation_during_command_marks_delivery_unknown_and_never_replays_or_runs_next_step()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var remote = new DelegateRemoteControl((_, _, token) =>
        {
            calls++;
            cts.Cancel();
            return Task.FromCanceled<RemoteResult>(token);
        });
        var runner = new ActivityRunner(remote, new RecordingDelayScheduler([]));

        var report = await runner.RunAsync(TwoActionActivity(Guid.NewGuid()), cancellationToken: cts.Token);

        Assert.Equal(1, calls);
        Assert.Equal(ActivityRunStatus.Cancelled, report.Status);
        Assert.Equal(ActivityStepRunStatus.Cancelled, report.Steps[0].Status);
        Assert.Equal(DeliveryState.Unknown, report.Steps[0].Delivery);
        Assert.Equal(ActivityStepRunStatus.NotRun, report.Steps[1].Status);
    }

    [Fact]
    public async Task Run_handler_loads_persisted_activity_and_delegates_to_runner()
    {
        var activity = new Activity(Guid.NewGuid(), "Film");
        var repository = new SingleActivityRepository(activity);
        var expected = new ActivityRunReport(
            activity.Id,
            activity.Name,
            ActivityFailurePolicy.StopOnFirstNonAccepted,
            ActivityRunStatus.Completed,
            []);
        var runner = new RecordingActivityRunner(expected);
        var handler = new RunActivityHandler(repository, runner);

        var actual = await handler.Handle(new RunActivity(activity.Id), CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal(activity.Id, runner.LastActivity?.Id);
        Assert.Equal(ActivityFailurePolicy.StopOnFirstNonAccepted, runner.LastPolicy);
    }

    [Fact]
    public async Task Run_handler_rejects_unknown_activity()
    {
        var handler = new RunActivityHandler(new SingleActivityRepository(null), new RecordingActivityRunner(null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new RunActivity(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public void Run_validator_rejects_unknown_failure_policy()
    {
        IValidator<RunActivity> validator = new RunActivityValidator();
        var result = validator.Validate(new RunActivity(Guid.NewGuid(), (ActivityFailurePolicy)999));
        Assert.False(result.IsValid);
    }

    private static Activity TwoActionActivity(Guid deviceId)
        => new(Guid.NewGuid(), "Two commands", steps:
        [
            new RemoteActionActivityStep(0, deviceId, RemoteActions.PowerToggle),
            new RemoteActionActivityStep(1, deviceId, RemoteActions.VolumeUp)
        ]);

    private sealed class DelegateRemoteControl(
        Func<Guid, RemoteAction, CancellationToken, Task<RemoteResult>> execute) : IRemoteControl
    {
        public Task<RemoteResult> ExecuteAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default)
            => execute(deviceId, action, cancellationToken);
    }

    private sealed class RecordingDelayScheduler(List<string> trace) : IActivityDelayScheduler
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            trace.Add($"delay:{duration.TotalMilliseconds:0}");
            return Task.CompletedTask;
        }
    }

    private sealed class DelegateDelayScheduler(
        Func<TimeSpan, CancellationToken, Task> delay) : IActivityDelayScheduler
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
            => delay(duration, cancellationToken);
    }

    private sealed class SingleActivityRepository(Activity? activity) : IActivityRepository
    {
        public Task<Activity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(activity is not null && activity.Id == id ? activity : null);

        public Task<IReadOnlyList<Activity>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Activity>>(activity is null ? [] : [activity]);

        public Task<Activity> SaveAsync(Activity value, CancellationToken cancellationToken = default)
            => Task.FromResult(value);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class RecordingActivityRunner(ActivityRunReport? result) : IActivityRunner
    {
        public Activity? LastActivity { get; private set; }
        public ActivityFailurePolicy? LastPolicy { get; private set; }

        public Task<ActivityRunReport> RunAsync(
            Activity activity,
            ActivityFailurePolicy failurePolicy = ActivityFailurePolicy.StopOnFirstNonAccepted,
            CancellationToken cancellationToken = default)
        {
            LastActivity = activity;
            LastPolicy = failurePolicy;
            return Task.FromResult(result ?? throw new InvalidOperationException("Runner should not be called."));
        }
    }
}
